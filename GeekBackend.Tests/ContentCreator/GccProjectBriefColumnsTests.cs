using System.Net;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GR1: the project holds its brief, and a Profile save neither touches it nor overwrites a newer row.
/// </summary>
/// <remarks>
/// What this cannot cover: the token is Postgres's <c>xmin</c>, and the read-to-write window inside one
/// request is enforced by the UPDATE's WHERE on it, which only a real Postgres runs. Covered here: the
/// model maps the token to xmin, a Profile save writes only Profile columns, and a write whose read is
/// stale is refused with nothing written.
/// </remarks>
public sealed class GccProjectBriefColumnsTests
{
    private const string Actor = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void The_project_carries_a_row_version_mapped_to_xmin()
    {
        using var context = new ContentCreatorDbContext(
            new DbContextOptionsBuilder<ContentCreatorDbContext>()
                .UseNpgsql("Host=model.invalid;Database=model_only;Username=none;Password=none")
                .Options);

        var version = context.Model.FindEntityType(typeof(GccProject))!.FindProperty(nameof(GccProject.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());
    }

    [Fact]
    public async Task A_profile_save_leaves_the_brief_exactly_as_it_is_in_the_database()
    {
        var options = Options();
        await using var db = new ContentCreatorDbContext(options);
        var project = await Seed(db);
        var repo = new GccProjectRepository(db);

        // The brief is saved through another context after this one loaded the row -- the interleaving a
        // whole-row Update(entity) turned into a lost brief.
        var loaded = await db.GccProjects.SingleAsync(p => p.Id == project.Id);
        await using (var other = new ContentCreatorDbContext(options))
        {
            var row = await other.GccProjects.SingleAsync(p => p.Id == project.Id);
            row.BriefJson = """{"angle":"newer"}""";
            row.Topic = "Accounts Payable: Automated Approval Workflows";
            await other.SaveChangesAsync();
        }

        var written = await repo.UpdateAsync(new UpdateGccProjectCommand(
            loaded.Id, Actor, "Renamed", loaded.StartDate, PartnerUrls: [], CompetitorUrls: []));

        Assert.False(written.Stale);
        await using var verify = new ContentCreatorDbContext(options);
        var stored = await verify.GccProjects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal("""{"angle":"newer"}""", stored.BriefJson);
        Assert.Equal("Accounts Payable: Automated Approval Workflows", stored.Topic);
    }

    [Fact]
    public async Task A_profile_save_from_a_stale_read_is_refused_and_nothing_is_written()
    {
        var options = Options();
        await using var db = new ContentCreatorDbContext(options);
        var project = await Seed(db);
        var repo = new GccProjectRepository(db);

        var loaded = await db.GccProjects.SingleAsync(p => p.Id == project.Id);
        await using (var other = new ContentCreatorDbContext(options))
        {
            // What Postgres does to xmin on every write; the in-memory provider has to be told.
            var row = await other.GccProjects.SingleAsync(p => p.Id == project.Id);
            row.Version = loaded.Version + 1;
            await other.SaveChangesAsync();
        }

        var written = await repo.UpdateAsync(new UpdateGccProjectCommand(
            loaded.Id, Actor, "Renamed", loaded.StartDate, PartnerUrls: [], CompetitorUrls: []));

        Assert.True(written.Stale);
        Assert.Null(written.Project);
        // The log entry is in the same transaction and rolls back with it on Postgres; the in-memory
        // provider ignores transactions, so only the project row is asserted here.
        await using var verify = new ContentCreatorDbContext(options);
        Assert.Equal("Q4 content programme", (await verify.GccProjects.SingleAsync(p => p.Id == project.Id)).Name);
    }

    [Fact]
    public async Task The_project_read_carries_its_brief_and_version()
    {
        await using var db = new ContentCreatorDbContext(Options());
        var project = await Seed(db, brief: """{"angle":"problem_solution"}""", topic: "AP: Approvals");

        var read = await new GccProjectRepository(db).GetByIdAsync(project.Id);

        Assert.Equal("""{"angle":"problem_solution"}""", read!.BriefJson);
        Assert.Equal("AP: Approvals", read.Topic);
        Assert.Equal(project.BriefVersion, read.Version);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("refused") });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true, false)]
    [InlineData(HttpStatusCode.Conflict, false, true)]
    public async Task GeekAPI_reads_a_missing_or_stale_project_as_an_answer_not_a_fault(
        HttpStatusCode status, bool notFound, bool stale)
    {
        var repo = new HttpGccRepository(
            new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);

        var written = await repo.UpdateProjectAsync(new UpdateGccProjectCommand(
            Guid.NewGuid(), Actor, "Name", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.Equal(notFound, written.NotFound);
        Assert.Equal(stale, written.Stale);
    }

    private static async Task<GccProject> Seed(ContentCreatorDbContext db, string? brief = null, string? topic = null)
    {
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
            Topic = topic,
            Version = 7,
            BriefVersion = 3,
        };
        db.AddRange(client, project);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return project;
    }

    private static DbContextOptions<ContentCreatorDbContext> Options() =>
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}
