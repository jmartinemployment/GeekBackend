using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A project has one page per type and name, and a Generate replaces its content. Nothing older is
/// kept (Jeff, 2026-10-06: "no history is required ... the old created content should be deleted").
/// </summary>
/// <remarks>
/// Two runs on project "test" on 2026-10-05 left two pillars, two blogs and nine tool pages; the page
/// showed the morning's under the afternoon's name and the export held both (Jeff: "you surprised me
/// keeping old version that wasn't clear or expected, what do I do with two versions"). The fix that
/// day kept the old text as the page's earlier version; the next day Jeff asked why anything was kept.
/// </remarks>
public sealed class GccProjectPageRepositoryTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Morning = new(2026, 10, 5, 10, 46, 32, DateTimeKind.Utc);
    private static readonly DateTime Afternoon = new(2026, 10, 5, 18, 32, 7, DateTimeKind.Utc);

    private static GccGeneratedPiece Piece(string type, string name, string body = "{}") =>
        new(type, name, body, """{"generatedByProvider":"OpenAi"}""");

    [Fact]
    public async Task The_first_generate_creates_a_page_for_each_piece_at_version_one()
    {
        var (options, projectId, createId) = await Seed();
        await using var db = new ContentCreatorDbContext(options);

        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals"), Piece("tool", "Ramp")]));

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Saved!.Count);
        Assert.All(result.Saved, s => Assert.True(s.NewPage));
        Assert.All(result.Saved, s => Assert.Equal(1, s.Version.VersionNumber));
        // Keyed to the project (GR4), and still stored under the run's create until the create table goes.
        Assert.All(result.Saved, s => Assert.Equal(projectId, s.Artifact.ProjectId));
        Assert.All(result.Saved, s => Assert.Equal(createId, s.Artifact.CreateId));
        Assert.All(await db.GccArtifacts.ToListAsync(), a => Assert.Equal(projectId, a.ProjectId));
        Assert.Equal(["pillar", "tool"], result.Saved.Select(s => s.Artifact.Type));
        Assert.Equal(2, await db.GccArtifacts.CountAsync());
        Assert.Equal(2, await db.GccArtifactVersions.CountAsync());
    }

    [Fact]
    public async Task A_second_generate_replaces_the_pages_content_and_keeps_nothing_older()
    {
        var (options, projectId, createId) = await Seed();
        Guid pageId, oldVersionId;
        await using (var first = new ContentCreatorDbContext(options))
        {
            var saved = await new GccProjectPageRepository(first).SaveGeneratedAsync(
                projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "morning")]));
            pageId = saved.Saved![0].Artifact.Id;
            oldVersionId = saved.Saved[0].Version.Id;
            // Approved, as the operator left it, with its evidence and its approval on record. The
            // next run's text is not what was approved, and the record is of text that is gone.
            (await first.GccArtifacts.SingleAsync()).Status = "approved";
            first.AddRange(
                new GccVersionEvidence { VersionId = oldVersionId, ProjectId = projectId, Provider = "OpenAi" },
                new GccApprovalEvent { ArtifactVersionId = oldVersionId, UserId = Owner });
            await first.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId,
            new SaveGccGeneratedPiecesCommand(
                createId, [Piece("pillar", "AP: Approvals", "afternoon"), Piece("blog", "AP: Approvals", "blog")]));

        var pillar = result.Saved!.Single(s => s.Artifact.Type == "pillar");
        Assert.False(pillar.NewPage);
        Assert.Equal(pageId, pillar.Artifact.Id);
        Assert.NotEqual(oldVersionId, pillar.Version.Id);
        Assert.Equal(1, pillar.Version.VersionNumber);
        Assert.Equal(1, pillar.Artifact.LatestVersionNumber);
        Assert.Equal("draft", pillar.Artifact.Status);
        Assert.True(result.Saved.Single(s => s.Artifact.Type == "blog").NewPage);

        // One pillar and one blog, each with one version. The morning's text, its evidence and its
        // approval are gone, not filed anywhere.
        Assert.Equal(["blog", "pillar"], (await db.GccArtifacts.Select(a => a.Type).ToListAsync()).Order());
        Assert.Equal(["afternoon"], await db.GccArtifactVersions.Where(v => v.ArtifactId == pageId).Select(v => v.BodyJson).ToListAsync());
        Assert.Null(await db.GccArtifactVersions.FirstOrDefaultAsync(v => v.Id == oldVersionId));
        Assert.Empty(await db.GccVersionEvidence.ToListAsync());
        Assert.Empty(await db.GccApprovalEvents.ToListAsync());
        Assert.Equal(2, await db.GccArtifactVersions.CountAsync());
    }

    [Fact]
    public async Task The_page_is_found_by_type_and_name_whatever_the_capitals_and_takes_the_new_spelling()
    {
        var (options, projectId, createId) = await Seed();
        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);
        await repo.SaveGeneratedAsync(projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("tool", "ApprovalMax", "first")]));

        var result = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("Tool", " Approvalmax ", "second"), Piece("tool", "Ramp")]));

        Assert.False(result.Saved![0].NewPage);
        Assert.Equal("Approvalmax", result.Saved[0].Artifact.Name);
        Assert.Equal(1, result.Saved[0].Version.VersionNumber);
        Assert.Equal(["second"], await db.GccArtifactVersions.Where(v => v.ArtifactId == result.Saved[0].Artifact.Id).Select(v => v.BodyJson).ToListAsync());
        // A different product is a different page; a blog of the same name is too.
        Assert.True(result.Saved[1].NewPage);
        Assert.Equal(2, await db.GccArtifacts.CountAsync());
    }

    [Fact]
    public async Task A_run_is_saved_whole_or_not_at_all()
    {
        var (options, projectId, createId) = await Seed();
        var elsewhere = Guid.NewGuid();
        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);

        var twice = await repo.SaveGeneratedAsync(
            projectId,
            new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP"), Piece("tool", "Ramp"), Piece("TOOL", "ramp")]));
        var foreignCreate = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(elsewhere, [Piece("pillar", "AP")]));
        var unnamed = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP"), Piece("tool", "  ")]));
        var nothing = await repo.SaveGeneratedAsync(projectId, new SaveGccGeneratedPiecesCommand(createId, []));
        var noProject = await repo.SaveGeneratedAsync(
            Guid.NewGuid(), new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP")]));

        Assert.Contains("None of the 3 piece(s) was saved", twice.Refusal!, StringComparison.Ordinal);
        Assert.Contains("the tool 'Ramp' 2 times", twice.Refusal!, StringComparison.Ordinal);
        Assert.Contains("is not one of this project's", foreignCreate.Refusal!, StringComparison.Ordinal);
        Assert.Contains("no type or no name", unnamed.Refusal!, StringComparison.Ordinal);
        Assert.Contains("nothing to save", nothing.Refusal!, StringComparison.Ordinal);
        Assert.True(noProject.ProjectNotFound);
        Assert.All(new[] { twice, foreignCreate, unnamed, nothing, noProject }, r => Assert.Null(r.Saved));
        Assert.Empty(await db.GccArtifacts.ToListAsync());
        Assert.Empty(await db.GccArtifactVersions.ToListAsync());
    }

    /// <summary>
    /// A refusal deletes nothing. A run that is refused on its third piece has not already replaced the
    /// content of its first two.
    /// </summary>
    [Fact]
    public async Task A_refused_run_leaves_every_pages_content_as_it_was()
    {
        var (options, projectId, createId) = await Seed();
        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);
        await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP", "kept"), Piece("tool", "Ramp", "kept too")]));

        var refused = await repo.SaveGeneratedAsync(
            projectId,
            new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP", "new"), Piece("tool", "Ramp", "new"), Piece("tool", "  ", "new")]));

        Assert.NotNull(refused.Refusal);
        await using var read = new ContentCreatorDbContext(options);
        Assert.Equal(["kept", "kept too"], (await read.GccArtifactVersions.Select(v => v.BodyJson).ToListAsync()).Order());
    }

    [Fact]
    public async Task Another_projects_page_of_the_same_name_is_not_this_projects_page()
    {
        var (options, projectId, createId) = await Seed();
        Guid theirCreate;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var theirs = Create(Guid.NewGuid());
            theirCreate = theirs.Id;
            var page = new GccArtifact { ProjectId = theirs.ProjectId, CreateId = theirs.Id, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            seed.AddRange(theirs, page, Version(page, Morning, "theirs"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals")]));

        Assert.True(result.Saved![0].NewPage);
        Assert.Equal(1, result.Saved[0].Version.VersionNumber);
        Assert.Equal(1, await db.GccArtifactVersions.CountAsync(v => v.BodyJson == "theirs"));
        Assert.Equal(1, await db.GccArtifacts.CountAsync(a => a.CreateId == theirCreate));
    }

    /// <summary>
    /// The project's pages are the drafts keyed to it, not the drafts of its creates. A draft under one
    /// of its creates that carries no project is on an unassigned create as far as this code knows, and
    /// keying it is the migration's backfill, never a guess made at save time.
    /// </summary>
    [Fact]
    public async Task A_page_is_the_projects_by_its_project_key_not_by_the_create_it_sits_under()
    {
        var (options, projectId, createId) = await Seed();
        Guid unkeyed;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var page = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            unkeyed = page.Id;
            seed.AddRange(page, Version(page, Morning, "unkeyed"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "keyed")]));

        Assert.True(result.Saved![0].NewPage);
        Assert.NotEqual(unkeyed, result.Saved[0].Artifact.Id);
        Assert.Equal(projectId, result.Saved[0].Artifact.ProjectId);
        Assert.Null((await db.GccArtifacts.SingleAsync(a => a.Id == unkeyed)).ProjectId);
        Assert.Equal(1, await db.GccArtifactVersions.CountAsync(v => v.BodyJson == "unkeyed"));
    }

    /// <summary>
    /// A page another page derives from -- a social post cut from the pillar -- is not the pillar, even
    /// when it shares the pillar's type and name. Only the project's own pages are matched.
    /// </summary>
    [Fact]
    public async Task A_derived_page_of_the_same_type_and_name_is_not_the_page_a_run_writes()
    {
        var (options, projectId, createId) = await Seed();
        Guid pillar, derived;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var page = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            var cut = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "pillar", Name = "AP: Approvals", ParentArtifactId = page.Id, CreatedAtUtc = Morning };
            (pillar, derived) = (page.Id, cut.Id);
            seed.AddRange(page, cut, Version(page, Morning, "pillar"), Version(cut, Morning, "cut from it"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "afternoon")]));

        Assert.False(result.Saved![0].NewPage);
        Assert.Equal(pillar, result.Saved[0].Artifact.Id);
        Assert.Equal(["afternoon"], await db.GccArtifactVersions.Where(v => v.ArtifactId == pillar).Select(v => v.BodyJson).ToListAsync());
        Assert.Equal(["cut from it"], await db.GccArtifactVersions.Where(v => v.ArtifactId == derived).Select(v => v.BodyJson).ToListAsync());
    }

    /// <summary>
    /// Two pages of one type and name on a project is what the database forbids. If it is ever seen,
    /// nothing is guessed about which one the run meant: the run is refused and nothing is touched.
    /// </summary>
    [Fact]
    public async Task Two_pages_of_one_type_and_name_refuse_the_run_rather_than_pick_one_and_nothing_is_deleted()
    {
        var (options, projectId, createId) = await Seed();
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var pillar = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            var am = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "tool", Name = "Ramp", CreatedAtUtc = Morning };
            var pm = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "tool", Name = "Ramp", CreatedAtUtc = Afternoon };
            seed.AddRange(pillar, am, pm, Version(pillar, Morning, "pillar"), Version(am, Morning, "morning"), Version(pm, Afternoon, "afternoon"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);
        // The pillar is reached first and its old content marked for deletion; Ramp refuses the run.
        var result = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "evening"), Piece("tool", "Ramp", "evening")]));
        // A later save through the same context must not carry the refused run's deletions with it.
        var later = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("blog", "AP: Approvals", "blog")]));

        Assert.Null(result.Saved);
        Assert.Contains("has 2 tool pages named 'Ramp'", result.Refusal!, StringComparison.Ordinal);
        Assert.NotNull(later.Saved);
        await using var read = new ContentCreatorDbContext(options);
        Assert.Equal(["afternoon", "blog", "morning", "pillar"], (await read.GccArtifactVersions.Select(v => v.BodyJson).ToListAsync()).Order());
    }

    [Fact]
    public async Task The_projects_draft_list_says_when_each_page_was_last_written_and_lists_the_newest_first()
    {
        var (options, projectId, createId) = await Seed();
        Guid pillar, approvalmax;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            // The pillar was created first and its content replaced this afternoon; Approvalmax was
            // created later in the morning and not written since, because the afternoon's run refused it.
            var page = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            var tool = new GccArtifact { ProjectId = projectId, CreateId = createId, Type = "tool", Name = "Approvalmax", CreatedAtUtc = Morning.AddMinutes(1) };
            (pillar, approvalmax) = (page.Id, tool.Id);
            seed.AddRange(
                page, tool,
                Version(page, Afternoon, "afternoon"),
                Version(tool, Morning.AddMinutes(1), "approvalmax"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var drafts = await new GccArtifactRepository(db).GetByProjectIdAsync(projectId);

        Assert.Equal([pillar, approvalmax], drafts.Select(d => d.Id));
        Assert.Equal((1, Afternoon), (drafts[0].LatestVersionNumber!.Value, drafts[0].LatestVersionAtUtc!.Value));
        Assert.Equal((1, Morning.AddMinutes(1)), (drafts[1].LatestVersionNumber!.Value, drafts[1].LatestVersionAtUtc!.Value));
    }

    private static async Task<(DbContextOptions<ContentCreatorDbContext> Options, Guid ProjectId, Guid CreateId)> Seed()
    {
        var options = new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ContentCreatorDbContext(options);
        var project = new GccProject
        {
            ClientId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid(),
            Name = "test",
            Status = GccProjectStatuses.Planned,
            StartDate = new DateOnly(2026, 10, 1),
            PartnerUrls = [],
            CompetitorUrls = [],
            Topic = "AP: Approvals",
        };
        var create = Create(project.Id);
        db.AddRange(project, create);
        await db.SaveChangesAsync();
        return (options, project.Id, create.Id);
    }

    private static GccCreate Create(Guid projectId) => new()
    {
        ClientId = Guid.NewGuid(), ProjectId = projectId, OwnerUserId = Owner,
        StartingContentType = "pillar", Topic = "AP: Approvals",
    };

    private static GccArtifactVersion Version(GccArtifact page, DateTime writtenAt, string body) =>
        new() { ArtifactId = page.Id, VersionNumber = 1, BodyJson = body, CreatedAtUtc = writtenAt };
}
