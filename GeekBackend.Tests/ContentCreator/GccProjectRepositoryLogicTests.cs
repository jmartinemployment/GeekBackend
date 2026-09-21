using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Business rules in GccTaskRepository and GccDeliverableRepository that do not depend on a
/// Postgres CHECK, trigger or unique index to hold — pure C# checked against a real
/// ContentCreatorDbContext, backed by EF's InMemory provider rather than a live database.
/// </summary>
/// <remarks>
/// What this cannot cover, and why: EF's InMemory provider does not enforce unique indexes or
/// CHECK constraints the way Postgres does, so it cannot verify the idempotency-key dedupe (relies
/// on a unique-index violation), the frozen-invoiced trigger, the composite-FK task/project match,
/// or any forbid_truncate trigger — those remain genuinely unverified without a real Postgres,
/// which the standing rule in this session is not to connect to directly. What it can verify is
/// everything this repository decides in C# before a row is ever written: the client-match check on
/// a deliverable, the billable-without-rate refusal, and — most load-bearing — that a rate snapshot
/// taken at write time does not move when the client's rate changes afterward.
/// </remarks>
public sealed class GccProjectRepositoryLogicTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Logged_time_takes_the_clients_rate_and_currency_at_write_time()
    {
        await using var db = Db();
        var client = await SeedClient(db, rate: 100m, currency: "USD");
        var project = await SeedProject(db, client.Id);
        var tasks = new GccTaskRepository(db);

        var result = await tasks.LogTimeAsync(
            new CreateGccTimeEntryCommand(project.Id, Actor.ToString("D"), Today(), 60, Billable: true),
            default);

        Assert.Null(result.Reason);
        Assert.NotNull(result.Entry);
        Assert.Equal(100m, result.Entry!.RateSnapshot);
        Assert.Equal("USD", result.Entry.Currency);

        // The client's rate changes after the entry was logged — an ordinary thing to happen
        // mid-engagement (a renegotiated retainer, a currency correction).
        var clientRow = await db.GccClients.SingleAsync(c => c.Id == client.Id);
        clientRow.Rate = 250m;
        clientRow.Currency = "EUR";
        await db.SaveChangesAsync();

        var reread = await db.GccTimeEntries.SingleAsync(e => e.Id == result.Entry.Id);

        // What this proves: the entry answers "what did this cost when it was logged", not
        // "what would this cost today". A billing report built from this table cannot be silently
        // rewritten by a rate change made after the fact.
        Assert.Equal(100m, reread.RateSnapshot);
        Assert.Equal("USD", reread.Currency);
    }

    [Fact]
    public async Task Billable_time_against_a_client_with_no_rate_is_refused()
    {
        await using var db = Db();
        var client = await SeedClient(db, rate: null, currency: "USD");
        var project = await SeedProject(db, client.Id);
        var tasks = new GccTaskRepository(db);

        var result = await tasks.LogTimeAsync(
            new CreateGccTimeEntryCommand(project.Id, Actor.ToString("D"), Today(), 60, Billable: true),
            default);

        Assert.Null(result.Entry);
        Assert.NotNull(result.Reason);
        Assert.Contains(client.Name, result.Reason);

        // The refusal writes nothing — not a row with a null rate standing in for "unknown".
        Assert.Empty(db.GccTimeEntries);
    }

    [Fact]
    public async Task Non_billable_time_against_a_client_with_no_rate_still_logs()
    {
        await using var db = Db();
        var client = await SeedClient(db, rate: null, currency: "USD");
        var project = await SeedProject(db, client.Id);
        var tasks = new GccTaskRepository(db);

        var result = await tasks.LogTimeAsync(
            new CreateGccTimeEntryCommand(project.Id, Actor.ToString("D"), Today(), 45, Billable: false),
            default);

        Assert.Null(result.Reason);
        Assert.NotNull(result.Entry);
        Assert.Null(result.Entry!.RateSnapshot);
        Assert.Null(result.Entry.Currency);
    }

    [Fact]
    public async Task A_deliverable_whose_create_belongs_to_another_client_is_refused()
    {
        await using var db = Db();
        var thisClient = await SeedClient(db, rate: 100m, currency: "USD");
        var otherClient = await SeedClient(db, rate: 100m, currency: "USD", name: "Other Co");
        var project = await SeedProject(db, thisClient.Id);
        var otherClientsCreate = await SeedCreate(db, otherClient.Id);
        var deliverables = new GccDeliverableRepository(db);

        var result = await deliverables.CreateAsync(
            new CreateGccDeliverableCommand(project.Id, otherClientsCreate.Id, "Pillar article", Actor.ToString("D")),
            default);

        Assert.Null(result.Deliverable);
        Assert.NotNull(result.Reason);
        Assert.Contains("different client", result.Reason);
        Assert.Empty(db.GccDeliverables);
    }

    [Fact]
    public async Task A_deliverable_whose_create_belongs_to_the_same_client_is_recorded()
    {
        await using var db = Db();
        var client = await SeedClient(db, rate: 100m, currency: "USD");
        var project = await SeedProject(db, client.Id);
        var create = await SeedCreate(db, client.Id);
        var deliverables = new GccDeliverableRepository(db);

        var result = await deliverables.CreateAsync(
            new CreateGccDeliverableCommand(project.Id, create.Id, "Pillar article", Actor.ToString("D")),
            default);

        Assert.Null(result.Reason);
        Assert.NotNull(result.Deliverable);
        Assert.Equal(create.Id, result.Deliverable!.CreateId);

        // One create, one deliverable — a second attempt to attach the same create is refused
        // rather than producing a second row for it.
        var second = await deliverables.CreateAsync(
            new CreateGccDeliverableCommand(project.Id, create.Id, "Pillar article, take two", Actor.ToString("D")),
            default);
        Assert.Null(second.Deliverable);
        Assert.NotNull(second.Reason);
        Assert.Single(db.GccDeliverables);
    }

    [Fact]
    public async Task Deleting_a_project_hides_it_but_keeps_its_row_and_log()
    {
        await using var db = Db();
        var client = await SeedClient(db, rate: 100m, currency: "USD");
        var project = await SeedProject(db, client.Id);
        var projects = new GccProjectRepository(db);

        var deleted = await projects.DeleteAsync(project.Id, Actor.ToString("D"), default);
        Assert.True(deleted);

        // Gone from every read this repository offers...
        Assert.Null(await projects.GetByIdAsync(project.Id, default));
        Assert.Empty(await projects.ListByClientIdAsync(client.Id, default));

        // ...but the row itself, and the log underneath it, are untouched — a real DELETE was
        // never on the table (the log's append-only trigger and its RESTRICT FK forbid it).
        var row = await db.GccProjects.SingleAsync(p => p.Id == project.Id);
        Assert.NotNull(row.DeletedAtUtc);
        var log = await db.GccProjectLog.Where(l => l.ProjectId == project.Id).ToListAsync();
        Assert.Contains(log, l => l.EventType == GccProjectLogEventTypes.ProjectDeleted);

        // Deleted is deleted: a second delete, or a further write, finds nothing to act on.
        Assert.False(await projects.DeleteAsync(project.Id, Actor.ToString("D"), default));
        Assert.Null(await projects.ChangeStatusAsync(
            new ChangeGccProjectStatusCommand(project.Id, Actor.ToString("D"), GccProjectStatuses.Active),
            default));
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<GccClient> SeedClient(
        ContentCreatorDbContext db, decimal? rate, string currency, string name = "Acme Co")
    {
        var client = new GccClient
        {
            Name = name,
            ContactName = "Jane Operator",
            ContactEmail = "jane@example.com",
            BillingEmail = "billing@example.com",
            PaymentTermsDays = 30,
            Rate = rate,
            Currency = currency,
        };
        db.GccClients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task<GccProject> SeedProject(ContentCreatorDbContext db, Guid clientId)
    {
        var project = new GccProject
        {
            ClientId = clientId,
            IdempotencyKey = Guid.NewGuid(),
            Name = "Q4 content programme",
            Status = GccProjectStatuses.Planned,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PartnerUrls = [],
            CompetitorUrls = [],
        };
        db.GccProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<GccCreate> SeedCreate(ContentCreatorDbContext db, Guid clientId)
    {
        var create = new GccCreate
        {
            ClientId = clientId,
            OwnerUserId = Actor,
            StartingContentType = "long-form",
            Topic = "ai chatbot implementation cost",
            Department = "marketing",
            Status = "draft",
        };
        db.GccCreates.Add(create);
        await db.SaveChangesAsync();
        return create;
    }

    // The repositories under test wrap every write in a BeginTransactionAsync/CommitAsync pair —
    // real, load-bearing behavior against Postgres (it is what makes a project/log insert atomic).
    // EF's InMemory provider does not support transactions at all and, by default, treats calling
    // BeginTransactionAsync as a logged warning escalated to an exception. Ignored here rather than
    // worked around in the repositories: the transaction call itself is correct production code,
    // and this is a property of the test double, not of what it is testing.
    private static ContentCreatorDbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
