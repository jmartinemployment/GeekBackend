using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A project has one page per type and name, and a Generate rewrites it as a new version
/// (fix-project-persistence J1: "Generate adds versions to the project").
/// </summary>
/// <remarks>
/// It did not. Two runs on project "test" on 2026-10-05 left two pillars, two blogs and nine tool
/// pages; the page showed the morning's under the afternoon's name and the export held both (Jeff:
/// "you surprised me keeping old version that wasn't clear or expected, what do I do with two
/// versions").
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
        Assert.All(result.Saved, s => Assert.Equal(createId, s.Artifact.CreateId));
        Assert.Equal(["pillar", "tool"], result.Saved.Select(s => s.Artifact.Type));
        Assert.Equal(2, await db.GccArtifacts.CountAsync());
        Assert.Equal(2, await db.GccArtifactVersions.CountAsync());
    }

    [Fact]
    public async Task A_second_generate_rewrites_the_page_as_its_next_version_and_adds_no_second_page()
    {
        var (options, projectId, createId) = await Seed();
        Guid pageId;
        await using (var first = new ContentCreatorDbContext(options))
        {
            var saved = await new GccProjectPageRepository(first).SaveGeneratedAsync(
                projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "morning")]));
            pageId = saved.Saved![0].Artifact.Id;
            // Approved, as the operator left it. The next run's text is not what was approved.
            (await first.GccArtifacts.SingleAsync()).Status = "approved";
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
        Assert.Equal(2, pillar.Version.VersionNumber);
        Assert.Equal(2, pillar.Artifact.LatestVersionNumber);
        Assert.Equal("draft", pillar.Artifact.Status);
        Assert.True(result.Saved.Single(s => s.Artifact.Type == "blog").NewPage);

        // One pillar and one blog. The morning's text is the pillar's first version, not a second pillar.
        Assert.Equal(["blog", "pillar"], (await db.GccArtifacts.Select(a => a.Type).ToListAsync()).Order());
        Assert.Equal(
            ["morning", "afternoon"],
            await db.GccArtifactVersions.Where(v => v.ArtifactId == pageId).OrderBy(v => v.VersionNumber).Select(v => v.BodyJson).ToListAsync());
    }

    [Fact]
    public async Task The_page_is_found_by_type_and_name_whatever_the_capitals_and_takes_the_new_spelling()
    {
        var (options, projectId, createId) = await Seed();
        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);
        await repo.SaveGeneratedAsync(projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("tool", "ApprovalMax")]));

        var result = await repo.SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("Tool", " Approvalmax "), Piece("tool", "Ramp")]));

        Assert.False(result.Saved![0].NewPage);
        Assert.Equal("Approvalmax", result.Saved[0].Artifact.Name);
        Assert.Equal(2, result.Saved[0].Version.VersionNumber);
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

    [Fact]
    public async Task Another_projects_page_of_the_same_name_is_not_this_projects_page()
    {
        var (options, projectId, createId) = await Seed();
        Guid theirCreate;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var theirs = Create(Guid.NewGuid());
            theirCreate = theirs.Id;
            var page = new GccArtifact { CreateId = theirs.Id, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            seed.AddRange(theirs, page, Version(page, 1, Morning, "theirs"));
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

    /// <summary>Project "test" as two runs left it: a morning and an afternoon draft of each page.</summary>
    [Fact]
    public async Task Merging_makes_the_older_drafts_of_a_page_its_earlier_versions()
    {
        var (options, projectId, createId) = await Seed();
        Guid morningPillar, afternoonPillar, approvalmax, derived;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var am = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning, Status = "approved" };
            var pm = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Afternoon };
            var onlyMorning = new GccArtifact { CreateId = createId, Type = "tool", Name = "Approvalmax", CreatedAtUtc = Morning };
            var social = new GccArtifact { CreateId = createId, Type = "social", Name = "From the pillar", ParentArtifactId = am.Id, CreatedAtUtc = Morning };
            (morningPillar, afternoonPillar, approvalmax, derived) = (am.Id, pm.Id, onlyMorning.Id, social.Id);
            seed.AddRange(
                am, pm, onlyMorning, social,
                Version(am, 1, Morning, "morning"),
                // Revised at midday: written after the morning's draft and before the afternoon's.
                Version(am, 2, Morning.AddHours(2), "morning, revised"),
                Version(pm, 1, Afternoon, "afternoon"),
                Version(onlyMorning, 1, Morning, "approvalmax"),
                Version(social, 1, Morning, "social"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccProjectPageRepository(db);
        var merged = await repo.MergeDuplicateDraftsAsync(projectId);
        var again = await repo.MergeDuplicateDraftsAsync(projectId);

        Assert.Equal((1, 1, 2), (merged.Pages, merged.DraftsMerged, merged.VersionsMoved));
        // Nothing left to merge the second time, and nothing changed by asking.
        Assert.Equal((0, 0, 0), (again.Pages, again.DraftsMerged, again.VersionsMoved));

        await using var read = new ContentCreatorDbContext(options);
        // The afternoon's draft is the page. The morning's is gone as a draft and kept as its history.
        Assert.Null(await read.GccArtifacts.FirstOrDefaultAsync(a => a.Id == morningPillar));
        Assert.Equal(
            [(1, "morning"), (2, "morning, revised"), (3, "afternoon")],
            (await read.GccArtifactVersions.Where(v => v.ArtifactId == afternoonPillar).OrderBy(v => v.VersionNumber).ToListAsync())
                .Select(v => (v.VersionNumber, v.BodyJson)));
        // A page with one draft is untouched, and what was derived from the merged draft follows it.
        Assert.Equal(1, await read.GccArtifactVersions.CountAsync(v => v.ArtifactId == approvalmax));
        Assert.Equal(afternoonPillar, (await read.GccArtifacts.SingleAsync(a => a.Id == derived)).ParentArtifactId);
        Assert.Equal(5, await read.GccArtifactVersions.CountAsync());
        Assert.True((await repo.MergeDuplicateDraftsAsync(Guid.NewGuid())).ProjectNotFound);
    }

    [Fact]
    public async Task A_generate_onto_a_page_with_duplicate_drafts_merges_them_in_the_same_write()
    {
        var (options, projectId, createId) = await Seed();
        Guid afternoon;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            var am = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            var pm = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Afternoon };
            afternoon = pm.Id;
            seed.AddRange(am, pm, Version(am, 1, Morning, "morning"), Version(pm, 1, Afternoon, "afternoon"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var result = await new GccProjectPageRepository(db).SaveGeneratedAsync(
            projectId, new SaveGccGeneratedPiecesCommand(createId, [Piece("pillar", "AP: Approvals", "evening")]));

        var saved = Assert.Single(result.Saved!);
        Assert.Equal(afternoon, saved.Artifact.Id);
        Assert.Equal(3, saved.Version.VersionNumber);
        Assert.Equal(afternoon, (await db.GccArtifacts.SingleAsync()).Id);
        Assert.Equal(
            ["morning", "afternoon", "evening"],
            await db.GccArtifactVersions.OrderBy(v => v.VersionNumber).Select(v => v.BodyJson).ToListAsync());
    }

    [Fact]
    public async Task The_projects_draft_list_says_when_each_page_was_last_written_and_lists_the_newest_first()
    {
        var (options, projectId, createId) = await Seed();
        Guid pillar, approvalmax;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            // The pillar was created first and rewritten this afternoon; Approvalmax was created later
            // in the morning and not rewritten since, because the afternoon's run refused it.
            var page = new GccArtifact { CreateId = createId, Type = "pillar", Name = "AP: Approvals", CreatedAtUtc = Morning };
            var tool = new GccArtifact { CreateId = createId, Type = "tool", Name = "Approvalmax", CreatedAtUtc = Morning.AddMinutes(1) };
            (pillar, approvalmax) = (page.Id, tool.Id);
            seed.AddRange(
                page, tool,
                Version(page, 1, Morning, "morning"),
                Version(page, 2, Afternoon, "afternoon"),
                Version(tool, 1, Morning.AddMinutes(1), "approvalmax"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var drafts = await new GccArtifactRepository(db).GetByProjectIdAsync(projectId);

        Assert.Equal([pillar, approvalmax], drafts.Select(d => d.Id));
        Assert.Equal((2, Afternoon), (drafts[0].LatestVersionNumber!.Value, drafts[0].LatestVersionAtUtc!.Value));
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

    private static GccArtifactVersion Version(GccArtifact page, int number, DateTime writtenAt, string body) =>
        new() { ArtifactId = page.Id, VersionNumber = number, BodyJson = body, CreatedAtUtc = writtenAt };
}
