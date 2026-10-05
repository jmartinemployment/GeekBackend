using System.Reflection;
using GeekAPI.Controllers.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GR3's dry run: the report says what the backfill would copy onto each project, names what the plan
/// leaves undecided, lists the creates with no project, and writes nothing.
/// </summary>
public sealed class GccBriefBackfillReportTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Day = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_newest_create_is_the_projects_brief_and_older_ones_are_revisions()
    {
        var options = Options();
        var project = await AddProject(options, "Accounts Payable");
        var older = await AddCreate(options, project, "AP: Approvals", Day, brief: "{}");
        var newer = await AddCreate(options, project, "AP: Approvals", Day.AddDays(3), brief: """{"angle":"x"}""", artifacts: 2);

        var report = await Report(options);

        var reported = Assert.Single(report.Projects);
        Assert.Equal("Accounts Payable", reported.ProjectName);
        Assert.Equal("Acme Co", reported.ClientName);
        Assert.Empty(reported.Decisions);
        Assert.Equal(
            [(newer.Id, GccBriefBackfillRoles.Current), (older.Id, GccBriefBackfillRoles.Revision)],
            reported.Creates.Select(c => (c.CreateId, c.Role)));
        Assert.Equal(2, reported.Creates[0].Artifacts);
        Assert.True(reported.Creates[0].HasBrief);
    }

    [Fact]
    public async Task A_project_that_already_has_its_own_brief_needs_a_decision()
    {
        var options = Options();
        var project = await AddProject(options, "Test", ownBrief: true);
        await AddCreate(options, project, "AP", Day, brief: "{}");

        var reported = Assert.Single((await Report(options)).Projects);

        Assert.True(reported.ProjectHasBrief);
        Assert.Equal(1, reported.ProjectRevisions);
        Assert.Contains(reported.Decisions, d => d.StartsWith("The project already has a brief of its own"));
    }

    [Fact]
    public async Task A_newest_create_with_no_brief_over_an_older_one_with_a_brief_needs_a_decision()
    {
        var options = Options();
        var project = await AddProject(options, "Payroll");
        await AddCreate(options, project, "Payroll: Old", Day, brief: "{}");
        await AddCreate(options, project, "Payroll: New", Day.AddDays(1), brief: null);

        var reported = Assert.Single((await Report(options)).Projects);

        Assert.Contains(reported.Decisions, d => d.Contains("has no brief, and an older one does"));
    }

    [Fact]
    public async Task A_project_whose_creates_carry_different_keywords_needs_a_decision()
    {
        var options = Options();
        var project = await AddProject(options, "Content Backfill Number 2");
        await AddCreate(options, project, "AP: Data Entry", Day, brief: "{}", artifacts: 5);
        await AddCreate(options, project, "AP: Approval Workflows", Day.AddDays(1), brief: "{}");
        var oneKeyword = await AddProject(options, "One keyword");
        await AddCreate(options, oneKeyword, "AP: Payments", Day, brief: "{}");
        await AddCreate(options, oneKeyword, "ap: payments ", Day.AddDays(1), brief: "{}");

        var report = await Report(options);

        var mixed = report.Projects.Single(p => p.ProjectName == "Content Backfill Number 2");
        var decision = Assert.Single(mixed.Decisions);
        Assert.StartsWith("Its creates carry 2 different keywords", decision);
        Assert.Contains("\"AP: Data Entry\" (5 drafts)", decision);
        Assert.Contains("would make it \"AP: Approval Workflows\"", decision);
        Assert.Empty(report.Projects.Single(p => p.ProjectName == "One keyword").Decisions);
    }

    [Fact]
    public async Task A_deleted_projects_creates_are_reported_not_dropped()
    {
        var options = Options();
        var project = await AddProject(options, "Gone", deleted: true);
        await AddCreate(options, project, "Gone: Topic", Day, brief: "{}");

        var reported = Assert.Single((await Report(options)).Projects);

        Assert.True(reported.ProjectDeleted);
        Assert.Contains(reported.Decisions, d => d.StartsWith("The project is deleted"));
    }

    [Fact]
    public async Task Creates_with_no_project_are_listed_and_counted_and_a_project_with_no_create_is_not_listed()
    {
        var options = Options();
        var withCreate = await AddProject(options, "Has one");
        await AddProject(options, "Has none");
        await AddCreate(options, withCreate, "On a project", Day, brief: "{}", artifacts: 1);
        var loose = await AddCreate(options, project: null, "Loose topic", Day.AddDays(2), brief: "{}", artifacts: 3);

        var report = await Report(options);

        var unassigned = Assert.Single(report.Unassigned);
        Assert.Equal((loose.Id, "Acme Co", "Loose topic", 3), (unassigned.CreateId, unassigned.ClientName, unassigned.Topic, unassigned.Artifacts));
        Assert.Equal("Has one", Assert.Single(report.Projects).ProjectName);
        Assert.Equal(
            new GccBriefBackfillCounts(
                Projects: 2, ProjectsWithCreates: 1, ProjectsNeedingADecision: 0,
                Creates: 2, CreatesOnProjects: 1, CreatesUnassigned: 1, Artifacts: 4, Revisions: 0),
            report.Counts);
    }

    [Fact]
    public async Task The_report_writes_nothing()
    {
        var options = Options();
        var project = await AddProject(options, "Accounts Payable");
        await AddCreate(options, project, "AP", Day, brief: """{"angle":"x"}""");

        await using var db = new ContentCreatorDbContext(options);
        await new GccBriefBackfillRepository(db).GetReportAsync();

        Assert.Empty(db.ChangeTracker.Entries());
        await using var verify = new ContentCreatorDbContext(options);
        var stored = await verify.GccProjects.SingleAsync();
        Assert.Null(stored.BriefJson);
        Assert.Equal(0, stored.BriefVersion);
        Assert.Empty(await verify.GccProjectRevisions.ToListAsync());
    }

    [Fact]
    public void GeekAPI_serves_it_only_under_the_internal_key_path()
    {
        var route = typeof(GccInternalController).GetCustomAttribute<RouteAttribute>()!.Template;
        var action = typeof(GccInternalController).GetMethod(nameof(GccInternalController.BriefBackfillReport))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template;

        // ApiKeyMiddleware requires X-API-Key on /api/{x}/internal/*.
        Assert.Equal("api/geek-content-creator/internal", route);
        Assert.Equal("brief-backfill/report", action);
        // The one thing under this controller that writes: the copy for a single named project.
        var writes = typeof(GccInternalController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpPatchAttribute or HttpDeleteAttribute))
            .Select(m => m.Name);
        Assert.Equal([nameof(GccInternalController.CopyBriefOntoProject)], writes);
    }

    /// <summary>
    /// The copy for one project: the one unambiguous case -- no brief of its own, exactly one create,
    /// which carries a brief -- and a refusal that writes nothing for every other.
    /// </summary>
    [Fact]
    public async Task One_projects_brief_is_copied_onto_it_as_a_backfill_revision_and_its_create_is_left_alone()
    {
        var options = Options();
        var project = await AddProject(options, "test");
        var create = await AddCreate(
            options, project, "Accounts Payable: Automated Approval Workflows", Day, brief: """{"angle":"problem_solution"}""", artifacts: 7);

        var result = await Copy(options, project.Id);

        Assert.Null(result.Refusal);
        Assert.Equal("""{"angle":"problem_solution"}""", result.Project!.BriefJson);
        Assert.Equal("Accounts Payable: Automated Approval Workflows", result.Project.Topic);
        Assert.Equal(1, result.Project.Version);
        Assert.Equal(GccProjectRevisionKinds.Backfill, result.Revision!.Kind);
        // The revision is the create's brief as its owner last saved it.
        Assert.Equal(create.UpdatedAtUtc, result.Revision.SavedAtUtc);
        Assert.Equal(Owner.ToString(), result.Revision.SavedBy);

        await using var verify = new ContentCreatorDbContext(options);
        var stored = await verify.GccProjects.SingleAsync();
        Assert.Equal(result.Project.BriefJson, stored.BriefJson);
        Assert.Equal("test", stored.Name);
        Assert.Single(await verify.GccProjectRevisions.ToListAsync());
        var untouched = await verify.GccCreates.SingleAsync();
        Assert.Equal("""{"angle":"problem_solution"}""", untouched.BriefJson);
        Assert.Equal(project.Id, untouched.ProjectId);
        Assert.Equal(7, await verify.GccArtifacts.CountAsync());
    }

    [Fact]
    public async Task A_second_copy_is_refused_and_so_is_a_project_with_its_own_brief()
    {
        var options = Options();
        var project = await AddProject(options, "test");
        await AddCreate(options, project, "AP", Day, brief: "{}");
        var own = await AddProject(options, "Has its own", ownBrief: true);
        await AddCreate(options, own, "AP", Day, brief: """{"would":"overwrite"}""");

        await Copy(options, project.Id);
        var again = await Copy(options, project.Id);
        var overOwn = await Copy(options, own.Id);

        Assert.Contains("already has a brief of its own", again.Refusal);
        Assert.Contains("already has a brief of its own", overOwn.Refusal);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Equal("""{"angle":"own"}""", (await verify.GccProjects.SingleAsync(p => p.Id == own.Id)).BriefJson);
        Assert.Equal(2, await verify.GccProjectRevisions.CountAsync());
    }

    [Fact]
    public async Task A_project_with_several_creates_none_or_one_without_a_brief_is_refused_and_nothing_is_written()
    {
        var options = Options();
        var several = await AddProject(options, "Several");
        await AddCreate(options, several, "AP: One", Day, brief: "{}");
        await AddCreate(options, several, "AP: Two", Day.AddDays(1), brief: "{}");
        var none = await AddProject(options, "None");
        var blank = await AddProject(options, "Blank");
        await AddCreate(options, blank, "AP", Day, brief: null);

        Assert.Contains("has 2 creates", (await Copy(options, several.Id)).Refusal);
        Assert.Contains("has 0 creates", (await Copy(options, none.Id)).Refusal);
        Assert.Contains("has no brief to copy", (await Copy(options, blank.Id)).Refusal);

        await using var verify = new ContentCreatorDbContext(options);
        Assert.All(await verify.GccProjects.ToListAsync(), p => Assert.Null(p.BriefJson));
        Assert.Empty(await verify.GccProjectRevisions.ToListAsync());
    }

    [Fact]
    public async Task A_deleted_or_unknown_project_is_not_found()
    {
        var options = Options();
        var gone = await AddProject(options, "Gone", deleted: true);
        await AddCreate(options, gone, "AP", Day, brief: "{}");

        Assert.True((await Copy(options, gone.Id)).NotFound);
        Assert.True((await Copy(options, Guid.NewGuid())).NotFound);
    }

    private static async Task<GccBriefCopyResult> Copy(DbContextOptions<ContentCreatorDbContext> options, Guid projectId)
    {
        await using var db = new ContentCreatorDbContext(options);
        return await new GccBriefBackfillRepository(db).CopyOntoProjectAsync(projectId);
    }

    private static async Task<GccBriefBackfillReport> Report(DbContextOptions<ContentCreatorDbContext> options)
    {
        await using var db = new ContentCreatorDbContext(options);
        return await new GccBriefBackfillRepository(db).GetReportAsync();
    }

    private static async Task<GccProject> AddProject(
        DbContextOptions<ContentCreatorDbContext> options, string name, bool ownBrief = false, bool deleted = false)
    {
        await using var db = new ContentCreatorDbContext(options);
        var client = await db.GccClients.FirstOrDefaultAsync();
        if (client is null)
        {
            client = new GccClient
            {
                Name = "Acme Co", ContactName = "Jane", ContactEmail = "jane@example.com",
                BillingEmail = "billing@example.com", PaymentTermsDays = 30, Currency = "USD",
            };
            db.Add(client);
        }

        var project = new GccProject
        {
            ClientId = client.Id,
            IdempotencyKey = Guid.NewGuid(),
            Name = name,
            Status = GccProjectStatuses.Planned,
            StartDate = DateOnly.FromDateTime(Day),
            PartnerUrls = [],
            CompetitorUrls = [],
            BriefJson = ownBrief ? """{"angle":"own"}""" : null,
            BriefVersion = ownBrief ? 1 : 0,
            DeletedAtUtc = deleted ? Day : null,
        };
        db.Add(project);
        if (ownBrief)
        {
            db.Add(new GccProjectRevision
            {
                ProjectId = project.Id, Kind = GccProjectRevisionKinds.Manual, BriefJson = project.BriefJson,
                SavedBy = Owner.ToString(), SavedAtUtc = Day,
            });
        }

        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<GccCreate> AddCreate(
        DbContextOptions<ContentCreatorDbContext> options,
        GccProject? project,
        string topic,
        DateTime createdAt,
        string? brief,
        int artifacts = 0)
    {
        await using var db = new ContentCreatorDbContext(options);
        var clientId = project?.ClientId ?? (await db.GccClients.FirstAsync()).Id;
        var create = new GccCreate
        {
            ClientId = clientId,
            ProjectId = project?.Id,
            OwnerUserId = Owner,
            StartingContentType = "pillar",
            Topic = topic,
            BriefJson = brief,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        };
        db.Add(create);
        for (var i = 0; i < artifacts; i++)
            db.Add(new GccArtifact { CreateId = create.Id, Type = "pillar", Name = $"{topic} {i}" });
        await db.SaveChangesAsync();
        return create;
    }

    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}
