using EphemeralMongo;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Atomic publish: a crawl is binary. A run in flight is invisible; the previously published run
/// keeps serving readers until the new one commits; the commit is a single-document status flip.
///
/// The shape this replaces purged the old run's pages up front and re-used its id, so a crawl that
/// died partway left the operator with neither the old corpus nor a usable new one.
/// </summary>
public sealed class MongoCrawlSlotPublishTests : IAsyncLifetime
{
    private IMongoRunner? _runner;
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        var envUrl = Environment.GetEnvironmentVariable("MONGO_CRAWLER_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            _connectionString = envUrl.Trim();
            return;
        }

        _runner = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            Version = MongoVersion.V7,
            Edition = MongoEdition.Community,
            AdditionalArguments = ["--quiet"],
        });
        _connectionString = _runner.ConnectionString;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_crawl_in_flight_never_displaces_the_published_run()
    {
        var mongo = CreateMongo();
        var owner = $"publish-{Guid.NewGuid():N}";
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(
            GeekCrawlerSeedNormalizer.NormalizeSeeds(["https://geekatyourspot.example/"]));

        var published = await SeedRunAsync(mongo, owner, seedKey, "complete", DateTimeOffset.UtcNow.AddHours(-2));

        // A re-crawl begins. It gets its own run; the published one is untouched.
        var staging = await SeedRunAsync(mongo, owner, seedKey, "external", DateTimeOffset.UtcNow);

        var visible = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.NotNull(visible);
        Assert.Equal(published.Id, visible!.Id);

        // Without the filter the newest run wins regardless of status — the bug this prevents.
        var unfiltered = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey);
        Assert.Equal(staging.Id, unfiltered!.Id);

        // The commit: one status flip on one document.
        await mongo.UpdateRunAsync(staging.Id, r =>
        {
            r.Status = "complete";
            r.CompletedAtUtc = DateTimeOffset.UtcNow;
        });

        var afterCommit = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.Equal(staging.Id, afterCommit!.Id);
    }

    [Fact]
    public async Task A_crawl_that_dies_leaves_the_published_run_serving()
    {
        var mongo = CreateMongo();
        var owner = $"publish-{Guid.NewGuid():N}";
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(
            GeekCrawlerSeedNormalizer.NormalizeSeeds(["https://died.example/"]));

        var published = await SeedRunAsync(mongo, owner, seedKey, "complete", DateTimeOffset.UtcNow.AddHours(-2));
        var staging = await SeedRunAsync(mongo, owner, seedKey, "external", DateTimeOffset.UtcNow);

        // The crawl dies. Nothing commits.
        var abandoned = await mongo.ListUncommittedRunsForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey);
        Assert.Single(abandoned);
        Assert.Equal(staging.Id, abandoned[0].Id);

        // The operator still has the previous corpus, whole. This is the property the old
        // purge-then-crawl shape destroyed.
        var visible = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.Equal(published.Id, visible!.Id);

        // Reclaiming the staging run does not disturb what is published.
        await mongo.DeleteRunAsync(staging.Id);
        Assert.Empty(await mongo.ListUncommittedRunsForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey));

        var stillVisible = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.Equal(published.Id, stillVisible!.Id);
    }

    [Fact]
    public async Task An_empty_slot_publishes_nothing_rather_than_a_run_in_flight()
    {
        var mongo = CreateMongo();
        var owner = $"publish-{Guid.NewGuid():N}";
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(
            GeekCrawlerSeedNormalizer.NormalizeSeeds(["https://first-crawl.example/"]));

        // First ever crawl of a site: nothing published yet.
        await SeedRunAsync(mongo, owner, seedKey, "external", DateTimeOffset.UtcNow);

        var visible = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.Null(visible);
    }

    [Fact]
    public async Task Deleting_a_run_takes_its_pages_with_it()
    {
        var mongo = CreateMongo();
        var owner = $"publish-{Guid.NewGuid():N}";
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(
            GeekCrawlerSeedNormalizer.NormalizeSeeds(["https://retired.example/"]));

        var run = await SeedRunAsync(mongo, owner, seedKey, "complete", DateTimeOffset.UtcNow);
        await mongo.CreatePagesBatchAsync(run.Id, [new GeekCrawlerPage
        {
            RunId = run.Id,
            Origin = "https://retired.example",
            Url = "https://retired.example/",
            FinalUrl = "https://retired.example/",
            StatusCode = 200,
            RobotsAllowed = true,
            Html = "<h1>Retired</h1>",
            CrawledAtUtc = DateTimeOffset.UtcNow,
        }]);

        Assert.Equal(1, await mongo.CountPagesByRunAsync(run.Id));

        await mongo.DeleteRunAsync(run.Id);

        // A run document removed while its pages survive would leave rows no slot lookup can reach.
        Assert.Equal(0, await mongo.CountPagesByRunAsync(run.Id));
        Assert.Null(await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey));
    }

    [Fact]
    public async Task An_aborted_crawl_keeps_its_record_and_loses_its_pages()
    {
        var mongo = CreateMongo();
        var owner = $"publish-{Guid.NewGuid():N}";
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(
            GeekCrawlerSeedNormalizer.NormalizeSeeds(["https://aborted.example/"]));

        var published = await SeedRunAsync(mongo, owner, seedKey, "complete", DateTimeOffset.UtcNow.AddHours(-2));
        var staging = await SeedRunAsync(mongo, owner, seedKey, "external", DateTimeOffset.UtcNow);

        await mongo.CreatePagesBatchAsync(staging.Id, [new GeekCrawlerPage
        {
            RunId = staging.Id,
            Origin = "https://aborted.example",
            Url = "https://aborted.example/",
            FinalUrl = "https://aborted.example/",
            StatusCode = 200,
            RobotsAllowed = true,
            Html = "<h1>Partial</h1>",
            CrawledAtUtc = DateTimeOffset.UtcNow,
        }]);
        Assert.Equal(1, await mongo.CountPagesByRunAsync(staging.Id));

        // The crawler reports failure: status recorded, prefix discarded.
        await mongo.UpdateRunAsync(staging.Id, r =>
        {
            r.Status = "failed";
            r.ErrorSummary = "connection reset at page 1";
        });
        await mongo.DeleteRunCrawlDataAsync(staging.Id);

        // The pages are gone -- nobody may read a prefix that never published.
        Assert.Equal(0, await mongo.CountPagesByRunAsync(staging.Id));

        // The record survives, so the operator can see why it failed.
        var record = await mongo.GetRunByIdAsync(staging.Id);
        Assert.NotNull(record);
        Assert.Equal("failed", record!.Status);
        Assert.Equal("connection reset at page 1", record.ErrorSummary);

        // And the slot still publishes the corpus it published before the failed attempt.
        var visible = await mongo.GetRunForSlotAsync(owner, CrawlTypes.ProjectSite, seedKey, publishedOnly: true);
        Assert.Equal(published.Id, visible!.Id);
    }

    private MongoGeekCrawlerService CreateMongo() =>
        new(_connectionString, NullLogger<MongoGeekCrawlerService>.Instance);

    private static async Task<GeekCrawlerRun> SeedRunAsync(
        IMongoGeekCrawlerService mongo,
        string ownerUserId,
        string seedKey,
        string status,
        DateTimeOffset createdAtUtc)
    {
        var run = await mongo.CreateRunAsync(new GeekCrawlerRun
        {
            OwnerUserId = ownerUserId,
            CrawlType = CrawlTypes.ProjectSite,
            Status = status,
            SeedUrlsJson = "[\"https://example.test/\"]",
            SeedKey = seedKey,
            CreatedAtUtc = createdAtUtc,
            StartedAtUtc = createdAtUtc,
        });
        return run;
    }
}
