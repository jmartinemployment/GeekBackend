using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A brief or research write made from a stale read is refused, never written over the newer row.
/// </summary>
/// <remarks>
/// <para>
/// brief_json and research_json were replaced whole, last writer wins, no token: the repository
/// called <c>Update(entity)</c>, which marks every column modified, so a brief-only save rewrote
/// research_json from whatever that request had loaded, and a research write did the same to the
/// brief.
/// </para>
/// <para>
/// What this cannot cover: the token is Postgres's <c>xmin</c>, and the read-to-write window inside
/// one request is enforced by the UPDATE's WHERE on that column, which only a real Postgres runs.
/// What it does cover is everything decided in C# -- that the model maps the token to xmin, that a
/// caller's stale <c>ExpectedVersion</c> is refused with nothing written, and that a write carrying
/// only a brief no longer touches research.
/// </para>
/// </remarks>
public sealed class GccCreateConcurrencyTests
{
    [Fact]
    public void The_create_carries_a_row_version_mapped_to_xmin()
    {
        using var context = new ContentCreatorDbContext(
            new DbContextOptionsBuilder<ContentCreatorDbContext>()
                .UseNpgsql("Host=model.invalid;Database=model_only;Username=none;Password=none")
                .Options);

        var version = context.Model.FindEntityType(typeof(GccCreate))!.FindProperty(nameof(GccCreate.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());
    }

    [Fact]
    public async Task A_write_from_a_stale_read_is_refused_and_nothing_is_written()
    {
        await using var db = Db();
        var create = await Seed(db, version: 7);
        var repo = new GccCreateRepository(db);

        var result = await repo.UpdateBriefResearchAsync(
            create.Id,
            new UpdateGccCreateBriefResearchCommand(
                BriefJson: """{"angle":"stale"}""", ResearchJson: null, ExpectedVersion: 6));

        Assert.True(result.Stale);
        Assert.Null(result.Create);
        var row = await db.GccCreates.AsNoTracking().SingleAsync(c => c.Id == create.Id);
        Assert.Equal("""{"angle":"current"}""", row.BriefJson);
    }

    [Fact]
    public async Task A_write_from_the_current_read_is_written()
    {
        await using var db = Db();
        var create = await Seed(db, version: 7);
        var repo = new GccCreateRepository(db);

        var result = await repo.UpdateBriefResearchAsync(
            create.Id,
            new UpdateGccCreateBriefResearchCommand(
                BriefJson: """{"angle":"new"}""", ResearchJson: null, ExpectedVersion: 7));

        Assert.False(result.Stale);
        Assert.Equal("""{"angle":"new"}""", result.Create!.BriefJson);
    }

    [Fact]
    public async Task A_brief_write_leaves_research_exactly_as_it_is_in_the_database()
    {
        var options = Options();
        await using var db = new ContentCreatorDbContext(options);
        var create = await Seed(db, version: 1);
        var repo = new GccCreateRepository(db);

        // A research write lands through another context after this one loaded the row -- the
        // interleaving a whole-row Update(entity) turned into lost research.
        var loaded = await db.GccCreates.SingleAsync(c => c.Id == create.Id);
        await using (var other = new ContentCreatorDbContext(options))
        {
            var row = await other.GccCreates.SingleAsync(c => c.Id == create.Id);
            row.ResearchJson = """{"quoteables":["newer"]}""";
            await other.SaveChangesAsync();
        }

        var result = await repo.UpdateBriefResearchAsync(
            loaded.Id, new UpdateGccCreateBriefResearchCommand("""{"angle":"new"}""", ResearchJson: null));

        Assert.False(result.Stale);
        await using var verify = new ContentCreatorDbContext(options);
        var stored = await verify.GccCreates.SingleAsync(c => c.Id == create.Id);
        Assert.Equal("""{"quoteables":["newer"]}""", stored.ResearchJson);
        Assert.Equal("""{"angle":"new"}""", stored.BriefJson);
    }

    [Fact]
    public async Task A_missing_create_is_not_found_rather_than_thrown()
    {
        await using var db = Db();
        var repo = new GccCreateRepository(db);

        var result = await repo.UpdateBriefResearchAsync(
            Guid.NewGuid(), new UpdateGccCreateBriefResearchCommand("{}", null));

        Assert.True(result.NotFound);
    }

    private static async Task<GccCreate> Seed(ContentCreatorDbContext db, uint version)
    {
        var create = new GccCreate
        {
            ClientId = Guid.NewGuid(),
            OwnerUserId = Guid.NewGuid(),
            StartingContentType = "pillar",
            Topic = "Accounts Payable: Automated Data Entry",
            BriefJson = """{"angle":"current"}""",
            ResearchJson = """{"quoteables":[]}""",
            Version = version,
        };
        db.GccCreates.Add(create);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return create;
    }

    private static ContentCreatorDbContext Db() => new(Options());

    /// <summary>One in-memory database per call; contexts built from the same options share it.</summary>
    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}
