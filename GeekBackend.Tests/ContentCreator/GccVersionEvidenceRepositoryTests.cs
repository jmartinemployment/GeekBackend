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
/// D2: what a version was made from is kept once per version, keyed to the project its draft is on.
/// </summary>
/// <remarks>
/// The unique index (one evidence row per version) is what holds under a race, and EF's InMemory
/// provider does not enforce it; the model test pins the index, and the check that gives the refusal
/// its sentence runs before the save and is covered here.
/// </remarks>
public sealed class GccVersionEvidenceRepositoryTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void One_row_per_version_is_a_unique_index_and_both_keys_restrict()
    {
        using var context = new ContentCreatorDbContext(
            new DbContextOptionsBuilder<ContentCreatorDbContext>()
                .UseNpgsql("Host=model.invalid;Database=model_only;Username=none;Password=none")
                .Options);
        var evidence = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GccVersionEvidence))!;

        Assert.Equal("gcc_version_evidence", evidence.GetTableName());
        Assert.True(evidence.GetIndexes().Single(i => i.GetDatabaseName() == "ux_gcc_version_evidence_version_id").IsUnique);
        Assert.Equal(2, evidence.GetForeignKeys().Count());
        Assert.All(evidence.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public async Task A_versions_evidence_is_written_once_read_back_whole_and_filed_under_its_own_project()
    {
        await using var db = Db();
        var (projectId, versionId) = await SeedVersion(db, onProject: true);
        var repo = new GccVersionEvidenceRepository(db);

        var written = await repo.CreateAsync(Evidence(versionId));
        var second = await repo.CreateAsync(Evidence(versionId));
        var read = await repo.GetByVersionIdAsync(versionId);

        Assert.NotNull(written.Evidence);
        Assert.Equal(projectId, written.Evidence!.ProjectId);
        Assert.Null(second.Evidence);
        Assert.Contains("already has its evidence", second.Refusal!, StringComparison.Ordinal);
        Assert.Equal("""[{"purpose":"body","system":"s","user":"u","response":"r","model":"m"}]""", read!.CallsJson);
        Assert.Equal("""[{"query":"q","runId":"x","scores":[0.8]}]""", read.RagQueriesJson);
        Assert.Single(await db.GccVersionEvidence.ToListAsync());
    }

    [Fact]
    public async Task Evidence_for_a_version_that_does_not_exist_or_is_on_no_project_is_refused()
    {
        await using var db = Db();
        var (_, loose) = await SeedVersion(db, onProject: false);
        var repo = new GccVersionEvidenceRepository(db);

        var missing = await repo.CreateAsync(Evidence(Guid.NewGuid()));
        var noProject = await repo.CreateAsync(Evidence(loose));

        Assert.Contains("does not exist", missing.Refusal!, StringComparison.Ordinal);
        Assert.Contains("is not on a project", noProject.Refusal!, StringComparison.Ordinal);
        Assert.Empty(await db.GccVersionEvidence.ToListAsync());
    }

    private static CreateGccVersionEvidenceCommand Evidence(Guid versionId) => new(
        versionId,
        "Anthropic",
        """["m"]""",
        """[{"purpose":"body","system":"s","user":"u","response":"r","model":"m"}]""",
        RagQueriesJson: """[{"query":"q","runId":"x","scores":[0.8]}]""");

    private static async Task<(Guid ProjectId, Guid VersionId)> SeedVersion(ContentCreatorDbContext db, bool onProject)
    {
        var projectId = Guid.NewGuid();
        var create = new GccCreate
        {
            ClientId = Guid.NewGuid(), ProjectId = onProject ? projectId : null, OwnerUserId = Owner,
            StartingContentType = "pillar", Topic = "Accounts Payable",
        };
        var artifact = new GccArtifact
        {
            ProjectId = create.ProjectId, CreateId = create.Id, Type = "pillar", Name = "Accounts Payable",
        };
        var version = new GccArtifactVersion { ArtifactId = artifact.Id, VersionNumber = 1, BodyJson = "{}" };
        db.AddRange(create, artifact, version);
        await db.SaveChangesAsync();
        return (projectId, version.Id);
    }

    private static ContentCreatorDbContext Db() =>
        new(new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
