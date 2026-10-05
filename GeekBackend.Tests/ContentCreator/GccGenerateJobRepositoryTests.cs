using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GA2, GeekRepository half: a Generate run on a project is a row, one running per project, recording
/// the brief revision it read, under the project's newest create or one minted for it.
/// </summary>
/// <remarks>
/// The in-memory provider has no partial unique index, so the race between the check and the insert is
/// Postgres's to enforce; the model test pins the index itself. Covered here: every refusal writes
/// nothing, and a run finishes once.
/// </remarks>
public sealed class GccGenerateJobRepositoryTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int BriefVersion = 3;

    [Fact]
    public void One_running_job_per_project_is_a_partial_unique_index_and_every_key_restricts()
    {
        using var context = new ContentCreatorDbContext(
            new DbContextOptionsBuilder<ContentCreatorDbContext>()
                .UseNpgsql("Host=model.invalid;Database=model_only;Username=none;Password=none")
                .Options);
        var job = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GccGenerateJob))!;

        var oneRunning = job.GetIndexes().Single(i => i.Name == "ux_gcc_generate_jobs_one_running_per_project");
        Assert.True(oneRunning.IsUnique);
        Assert.Equal("status = 'running'", oneRunning.GetFilter());
        Assert.Equal([nameof(GccGenerateJob.ProjectId)], oneRunning.Properties.Select(p => p.Name));
        Assert.Equal("status IN ('running', 'ready', 'failed')", Assert.Single(job.GetCheckConstraints()).Sql);
        Assert.Equal(3, job.GetForeignKeys().Count());
        Assert.All(job.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public async Task A_project_with_no_create_gets_one_minted_and_the_run_records_the_newest_revision()
    {
        var options = Options();
        var (project, newest) = await Seed(options);

        var started = await Start(options, project.Id, createId: null);

        var job = started.Job!;
        Assert.Equal(newest.Id, job.BriefRevisionId);
        Assert.Equal(newest.SavedAtUtc, job.BriefRevisionSavedAtUtc);
        Assert.Equal(GccGenerateJobStatuses.Running, job.Status);
        await using var verify = new ContentCreatorDbContext(options);
        var minted = await verify.GccCreates.SingleAsync();
        Assert.Equal(job.CreateId, minted.Id);
        Assert.Equal(project.Id, minted.ProjectId);
        Assert.Equal("AP: Approvals", minted.Topic);
        Assert.Equal("pillar", minted.StartingContentType);
        Assert.Null(minted.BriefJson);
    }

    [Fact]
    public async Task The_backing_create_is_the_projects_newest_and_a_run_is_stored_under_it()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var older = await AddCreate(options, project.Id, DateTime.UtcNow.AddDays(-2));
        var newer = await AddCreate(options, project.Id, DateTime.UtcNow.AddDays(-1));

        await using (var db = new ContentCreatorDbContext(options))
        {
            Assert.Equal(newer.Id, (await new GccGenerateJobRepository(db).GetBackingCreateAsync(project.Id))!.Id);
        }

        var started = await Start(options, project.Id, newer.Id);

        Assert.Equal(newer.Id, started.Job!.CreateId);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Equal(2, await verify.GccCreates.CountAsync());
        Assert.NotEqual(older.Id, started.Job.CreateId);
    }

    [Fact]
    public async Task A_create_from_another_project_is_refused()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var (other, _) = await Seed(options);
        var foreign = await AddCreate(options, other.Id, DateTime.UtcNow);

        var started = await Start(options, project.Id, foreign.Id);

        Assert.True(started.CreateNotOnProject);
        await AssertNoJobs(options);
    }

    [Fact]
    public async Task A_second_start_while_one_runs_is_refused_naming_the_running_one()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var first = await Start(options, project.Id, createId: null);

        var second = await Start(options, project.Id, first.Job!.CreateId);

        Assert.Null(second.Job);
        Assert.Equal(first.Job.Id, second.AlreadyRunning!.Id);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Single(await verify.GccGenerateJobs.ToListAsync());
    }

    [Fact]
    public async Task A_brief_saved_after_the_read_refuses_the_start()
    {
        var options = Options();
        var (project, _) = await Seed(options);

        var started = await Start(options, project.Id, createId: null, expectedBriefVersion: BriefVersion - 1);

        Assert.True(started.StaleBrief);
        await AssertNoJobs(options);
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Empty(await verify.GccCreates.ToListAsync());
    }

    [Fact]
    public async Task A_project_whose_brief_was_never_saved_or_that_does_not_exist_is_refused()
    {
        var options = Options();
        var (project, _) = await Seed(options, withRevision: false);

        Assert.True((await Start(options, project.Id, createId: null, expectedBriefVersion: 0)).NoSavedBrief);
        Assert.True((await Start(options, Guid.NewGuid(), createId: null)).ProjectNotFound);
        await AssertNoJobs(options);
    }

    [Fact]
    public async Task A_run_finishes_once()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var job = (await Start(options, project.Id, createId: null)).Job!;

        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccGenerateJobRepository(db);
        var done = await repo.CompleteAsync(job.Id, """{"created":[]}""");
        var again = await repo.FailAsync(job.Id, "late failure");

        Assert.Equal(GccGenerateJobStatuses.Ready, done!.Status);
        Assert.NotNull(done.FinishedAtUtc);
        Assert.Null(again);
        Assert.Equal(GccGenerateJobStatuses.Ready, (await repo.GetByIdAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task The_projects_newest_run_is_the_one_going_now_or_the_last_to_end()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var (other, _) = await Seed(options);

        await using (var db = new ContentCreatorDbContext(options))
            Assert.Null(await new GccGenerateJobRepository(db).GetLatestForProjectAsync(project.Id));

        var first = await Start(options, project.Id, createId: null);
        await using (var db = new ContentCreatorDbContext(options))
            await new GccGenerateJobRepository(db).CompleteAsync(first.Job!.Id, """{"created":[]}""");
        await using (var db = new ContentCreatorDbContext(options))
        {
            var ended = await new GccGenerateJobRepository(db).GetLatestForProjectAsync(project.Id);
            Assert.Equal(first.Job!.Id, ended!.Id);
            Assert.Equal(GccGenerateJobStatuses.Ready, ended.Status);
            Assert.Equal("""{"created":[]}""", ended.ResultJson);
        }

        var second = await Start(options, project.Id, first.Job!.CreateId);
        await Start(options, other.Id, createId: null);
        await using (var read = new ContentCreatorDbContext(options))
        {
            var running = await new GccGenerateJobRepository(read).GetLatestForProjectAsync(project.Id);
            Assert.Equal(second.Job!.Id, running!.Id);
            Assert.Equal(GccGenerateJobStatuses.Running, running.Status);
        }
    }

    [Fact]
    public async Task Startup_fails_every_running_run_and_frees_the_project()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var (other, _) = await Seed(options);
        var running = (await Start(options, project.Id, createId: null)).Job!;
        var finished = (await Start(options, other.Id, createId: null)).Job!;
        await using (var db = new ContentCreatorDbContext(options))
        {
            await new GccGenerateJobRepository(db).CompleteAsync(finished.Id, "{}");
        }

        await using (var db = new ContentCreatorDbContext(options))
        {
            Assert.Equal(1, await new GccGenerateJobRepository(db).FailInterruptedAsync("interrupted by redeploy"));
        }

        await using var verify = new ContentCreatorDbContext(options);
        var repo = new GccGenerateJobRepository(verify);
        Assert.Equal("interrupted by redeploy", (await repo.GetByIdAsync(running.Id))!.Error);
        Assert.Equal(GccGenerateJobStatuses.Ready, (await repo.GetByIdAsync(finished.Id))!.Status);
        Assert.NotNull((await Start(options, project.Id, running.CreateId)).Job);
    }

    [Fact]
    public async Task Deleting_a_create_deletes_the_runs_that_wrote_under_it()
    {
        var options = Options();
        var (project, _) = await Seed(options);
        var job = (await Start(options, project.Id, createId: null)).Job!;

        await using (var db = new ContentCreatorDbContext(options))
        {
            Assert.True(await new GccCreateRepository(db).DeleteAsync(job.CreateId));
        }

        await AssertNoJobs(options);
    }

    private static async Task<GccGenerateJobStartResult> Start(
        DbContextOptions<ContentCreatorDbContext> options,
        Guid projectId,
        Guid? createId,
        int expectedBriefVersion = BriefVersion)
    {
        await using var db = new ContentCreatorDbContext(options);
        return await new GccGenerateJobRepository(db).StartAsync(new StartGccGenerateJobCommand(
            Guid.NewGuid(), projectId, createId, expectedBriefVersion, Owner, ["pillar", "tool"], "Anthropic"));
    }

    private static async Task AssertNoJobs(DbContextOptions<ContentCreatorDbContext> options)
    {
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Empty(await verify.GccGenerateJobs.ToListAsync());
    }

    private static async Task<GccCreate> AddCreate(
        DbContextOptions<ContentCreatorDbContext> options, Guid projectId, DateTime createdAt)
    {
        await using var db = new ContentCreatorDbContext(options);
        var project = await db.GccProjects.SingleAsync(p => p.Id == projectId);
        var create = new GccCreate
        {
            ClientId = project.ClientId,
            ProjectId = projectId,
            OwnerUserId = Owner,
            StartingContentType = "pillar",
            Topic = "AP: Approvals",
            CreatedAtUtc = createdAt,
        };
        db.GccCreates.Add(create);
        await db.SaveChangesAsync();
        return create;
    }

    private static async Task<(GccProject Project, GccProjectRevision Newest)> Seed(
        DbContextOptions<ContentCreatorDbContext> options, bool withRevision = true)
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
            BriefJson = withRevision ? """{"angle":"problem_solution"}""" : null,
            Topic = "AP: Approvals",
            BriefVersion = withRevision ? BriefVersion : 0,
        };
        db.AddRange(client, project);
        GccProjectRevision newest = null!;
        if (withRevision)
        {
            var older = new GccProjectRevision
            {
                ProjectId = project.Id, Kind = GccProjectRevisionKinds.Manual, BriefJson = "{}",
                Topic = "AP", SavedBy = Owner.ToString(), SavedAtUtc = DateTime.UtcNow.AddHours(-1),
            };
            newest = new GccProjectRevision
            {
                ProjectId = project.Id, Kind = GccProjectRevisionKinds.Manual, BriefJson = project.BriefJson,
                Topic = project.Topic, SavedBy = Owner.ToString(), SavedAtUtc = DateTime.UtcNow,
            };
            db.AddRange(older, newest);
        }
        await db.SaveChangesAsync();
        return (project, newest);
    }

    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}
