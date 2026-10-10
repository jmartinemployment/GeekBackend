using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Writing a page's text one page at a time replaces what the page had. One version per page; nothing
/// older is kept (Jeff, 2026-10-06).
/// </summary>
public sealed class GccArtifactVersionReplaceTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Writing_a_pages_text_deletes_the_old_text_its_evidence_and_its_approval_and_the_page_is_a_draft_again()
    {
        var options = Options();
        Guid projectId, pageId, oldVersionId;
        await using (var seed = new ContentCreatorDbContext(options))
        {
            projectId = Guid.NewGuid();
            var create = new GccCreate
            {
                ClientId = Guid.NewGuid(), ProjectId = projectId, OwnerUserId = Owner,
                StartingContentType = "pillar", Topic = "Accounts Payable",
            };
            var page = new GccArtifact
            {
                ProjectId = projectId, CreateId = create.Id, Type = "pillar", Name = "Accounts Payable", Status = "approved",
            };
            var old = new GccArtifactVersion { ArtifactId = page.Id, VersionNumber = 1, BodyJson = "before" };
            (pageId, oldVersionId) = (page.Id, old.Id);
            seed.AddRange(
                create, page, old,
                new GccVersionEvidence { VersionId = old.Id, ProjectId = projectId, Provider = "Anthropic" },
                new GccApprovalEvent { ArtifactVersionId = old.Id, UserId = Owner });
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var written = await new GccArtifactVersionRepository(db).CreateAsync(
            new CreateGccArtifactVersionCommand(pageId, "after", """{"generatedByProvider":"Anthropic"}"""));

        Assert.NotNull(written);
        Assert.NotEqual(oldVersionId, written!.Id);
        Assert.Equal(1, written.VersionNumber);
        Assert.Equal("after", written.BodyDocumentJson);

        await using var read = new ContentCreatorDbContext(options);
        var versions = await read.GccArtifactVersions.Where(v => v.ArtifactId == pageId).ToListAsync();
        Assert.Equal([written.Id], versions.Select(v => v.Id));
        Assert.Empty(await read.GccVersionEvidence.ToListAsync());
        Assert.Empty(await read.GccApprovalEvents.ToListAsync());
        Assert.Equal("draft", (await read.GccArtifacts.SingleAsync(a => a.Id == pageId)).Status);
        Assert.Equal(
            [written.Id],
            (await new GccArtifactVersionRepository(read).GetByArtifactIdAsync(pageId)).Select(v => v.Id));
    }

    [Fact]
    public async Task Text_for_a_page_that_does_not_exist_is_not_written()
    {
        await using var db = new ContentCreatorDbContext(Options());

        var written = await new GccArtifactVersionRepository(db).CreateAsync(
            new CreateGccArtifactVersionCommand(Guid.NewGuid(), "orphan"));

        Assert.Null(written);
        Assert.Empty(await db.GccArtifactVersions.ToListAsync());
    }

    /// <summary>The database holds the rule, not only the code: one version per page, by unique index.</summary>
    [Fact]
    public void One_version_per_page_is_a_unique_index()
    {
        using var db = new ContentCreatorDbContext(Options());
        var index = db.Model.FindEntityType(typeof(GccArtifactVersion))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(GccArtifactVersion.ArtifactId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("ux_gcc_artifact_versions_artifact_id", index.GetDatabaseName());
    }

    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
}
