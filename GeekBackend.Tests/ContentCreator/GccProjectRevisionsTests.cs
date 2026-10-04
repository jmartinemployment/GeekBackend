using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using RepoProjectsController = GeekRepository.Controllers.ContentCreator.GccProjectsController;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GR2: every brief save is a revision, written with the project in one SaveChanges, and a save from a
/// stale read is refused with nothing written.
/// </summary>
/// <remarks>
/// The in-memory provider does not bump xmin and ignores transactions, so the race inside one request
/// is Postgres's to enforce; covered here is the expected-version refusal, which is checked before
/// any write.
/// </remarks>
public sealed class GccProjectRevisionsTests
{
    private const string Jeff = "11111111-1111-1111-1111-111111111111";
    private const uint SeededVersion = 7;

    [Fact]
    public void The_revisions_table_allows_only_manual_and_backfill_and_restricts_the_project_key()
    {
        using var context = new ContentCreatorDbContext(
            new DbContextOptionsBuilder<ContentCreatorDbContext>()
                .UseNpgsql("Host=model.invalid;Database=model_only;Username=none;Password=none")
                .Options);

        // Check constraints live only in the design-time model; the runtime one drops them.
        var revision = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GccProjectRevision))!;

        Assert.Equal("gcc_project_revisions", revision.GetTableName());
        var kind = Assert.Single(revision.GetCheckConstraints());
        Assert.Equal("kind IN ('manual', 'backfill')", kind.Sql);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(revision.GetForeignKeys()).DeleteBehavior);
    }

    [Fact]
    public async Task A_save_writes_the_brief_and_one_revision_and_leaves_the_profile_alone()
    {
        var options = Options();
        var project = await Seed(options);

        var saved = await Save(options, project.Id, """{"angle":"problem_solution"}""", " AP: Approvals ");

        Assert.False(saved.Stale);
        Assert.Equal("""{"angle":"problem_solution"}""", saved.Project!.BriefJson);
        Assert.Equal("AP: Approvals", saved.Revision!.Topic);
        await using var verify = new ContentCreatorDbContext(options);
        var row = await verify.GccProjects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("Q4 content programme", row.Name);
        Assert.Equal("AP: Approvals", row.Topic);
        var revision = await verify.GccProjectRevisions.SingleAsync();
        Assert.Equal(GccProjectRevisionKinds.Manual, revision.Kind);
        Assert.Equal(Jeff, revision.SavedBy);
        Assert.Equal(row.BriefJson, revision.BriefJson);
    }

    [Fact]
    public async Task A_save_from_an_older_read_is_refused_and_nothing_is_written()
    {
        var options = Options();
        var project = await Seed(options, brief: """{"angle":"newer"}""");

        var saved = await Save(options, project.Id, """{"angle":"older"}""", "Old", expectedVersion: SeededVersion - 1);

        Assert.True(saved.Stale);
        Assert.Null(saved.Project);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Equal("""{"angle":"newer"}""", (await verify.GccProjects.SingleAsync()).BriefJson);
        Assert.Empty(await verify.GccProjectRevisions.ToListAsync());
    }

    [Fact]
    public async Task A_missing_or_deleted_project_is_not_found()
    {
        var options = Options();
        var project = await Seed(options, deleted: true);

        Assert.True((await Save(options, project.Id, "{}", null)).NotFound);
        Assert.True((await Save(options, Guid.NewGuid(), "{}", null)).NotFound);
    }

    [Fact]
    public async Task Every_save_that_changes_something_adds_a_row_and_none_is_altered()
    {
        var options = Options();
        var project = await Seed(options);

        await Save(options, project.Id, """{"step":1}""", "One");
        await Save(options, project.Id, """{"step":2}""", "One");
        await Save(options, project.Id, """{"step":2}""", "Two");

        await using var verify = new ContentCreatorDbContext(options);
        var rows = await verify.GccProjectRevisions.OrderBy(r => r.SavedAtUtc).ToListAsync();
        Assert.Equal(
            [("""{"step":1}""", "One"), ("""{"step":2}""", "One"), ("""{"step":2}""", "Two")],
            rows.Select(r => (r.BriefJson, r.Topic)));
        Assert.All(rows, r => Assert.Equal(GccProjectRevisionKinds.Manual, r.Kind));
    }

    [Fact]
    public async Task A_save_identical_to_the_newest_revision_writes_nothing_and_returns_it()
    {
        var options = Options();
        var project = await Seed(options);
        var first = await Save(options, project.Id, """{"angle":"problem_solution"}""", "AP: Approvals");

        // Blank topic resolves to the stored keyword, so this is the same brief and keyword again.
        var again = await Save(options, project.Id, """{"angle":"problem_solution"}""", "  ");

        Assert.False(again.Stale);
        Assert.Equal(first.Revision!.Id, again.Revision!.Id);
        Assert.Equal(first.Revision.SavedAtUtc, again.Project!.BriefSavedAtUtc);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Single(await verify.GccProjectRevisions.ToListAsync());
        Assert.Equal(first.Project!.UpdatedAtUtc, (await verify.GccProjects.SingleAsync()).UpdatedAtUtc);
    }

    [Fact]
    public async Task An_identical_save_from_an_older_read_is_still_refused()
    {
        var options = Options();
        var project = await Seed(options);
        await Save(options, project.Id, "{}", "AP");

        var stale = await Save(options, project.Id, "{}", "AP", expectedVersion: SeededVersion - 1);

        Assert.True(stale.Stale);
    }

    [Fact]
    public async Task The_route_refuses_a_brief_that_is_not_json()
    {
        var options = Options();
        var project = await Seed(options);
        await using var db = new ContentCreatorDbContext(options);
        var controller = new RepoProjectsController(
            new GccProjectRepository(db), NullLogger<RepoProjectsController>.Instance);

        var result = await controller.SaveBrief(project.Id,
            new SaveGccProjectBriefCommand(project.Id, Jeff, "{not json", null, SeededVersion),
            CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.StartsWith("briefJson must be a JSON document", (string)refused.Value!);
        Assert.Empty(await db.GccProjectRevisions.ToListAsync());
    }

    [Fact]
    public async Task A_blank_topic_leaves_the_keyword_as_it_is()
    {
        var options = Options();
        var project = await Seed(options);
        await Save(options, project.Id, "{}", "AP: Approvals");

        var saved = await Save(options, project.Id, """{"more":1}""", "  ");

        Assert.Equal("AP: Approvals", saved.Project!.Topic);
        Assert.Equal("AP: Approvals", saved.Revision!.Topic);
    }

    [Fact]
    public async Task The_project_read_says_when_its_brief_was_saved_and_null_before_the_first()
    {
        var options = Options();
        var project = await Seed(options);
        await using (var db = new ContentCreatorDbContext(options))
        {
            Assert.Null((await new GccProjectRepository(db).GetByIdAsync(project.Id))!.BriefSavedAtUtc);
        }

        var saved = await Save(options, project.Id, "{}", null);

        await using var verify = new ContentCreatorDbContext(options);
        var repo = new GccProjectRepository(verify);
        Assert.Equal(saved.Revision!.SavedAtUtc, (await repo.GetByIdAsync(project.Id))!.BriefSavedAtUtc);
        Assert.Equal(
            saved.Revision.SavedAtUtc,
            Assert.Single(await repo.ListByClientIdAsync(project.ClientId)).BriefSavedAtUtc);
    }

    [Fact]
    public async Task An_incomplete_brief_saves()
    {
        var options = Options();
        var project = await Seed(options);

        var saved = await Save(options, project.Id, """{"angle":null}""", null);

        Assert.False(saved.Stale);
        Assert.False(saved.NotFound);
        Assert.Null(saved.Project!.Topic);
    }

    private static async Task<GccProjectBriefSaveResult> Save(
        DbContextOptions<ContentCreatorDbContext> options,
        Guid projectId,
        string? brief,
        string? topic,
        uint expectedVersion = SeededVersion)
    {
        // A fresh context per save, as each request has.
        await using var db = new ContentCreatorDbContext(options);
        return await new GccProjectRepository(db).SaveBriefAsync(
            new SaveGccProjectBriefCommand(projectId, Jeff, brief, topic, expectedVersion));
    }

    private static async Task<GccProject> Seed(
        DbContextOptions<ContentCreatorDbContext> options, string? brief = null, bool deleted = false)
    {
        await using var db = new ContentCreatorDbContext(options);
        var client = new GccClient
        {
            Name = "Acme Co", ContactName = "Jane", ContactEmail = "jane@example.com",
            BillingEmail = "billing@example.com", PaymentTermsDays = 30, Currency = "USD",
        };
        var project = new GccProject
        {
            ClientId = client.Id,
            IdempotencyKey = Guid.NewGuid(),
            Name = "Q4 content programme",
            Status = GccProjectStatuses.Planned,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PartnerUrls = [],
            CompetitorUrls = [],
            BriefJson = brief,
            Version = SeededVersion,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
        };
        db.AddRange(client, project);
        await db.SaveChangesAsync();
        return project;
    }

    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}
