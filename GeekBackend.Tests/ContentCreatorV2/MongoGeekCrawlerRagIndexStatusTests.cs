using EphemeralMongo;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// UpdateRagIndexStatusAsync exists specifically to avoid UpdateRunAsync's find-then-
/// ReplaceOneAsync race: a concurrent crawl-progress write on the same run document must survive
/// a RAG index-status update landing at the same time, and vice versa.
/// </summary>
public sealed class MongoGeekCrawlerRagIndexStatusTests : IAsyncLifetime
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
    public async Task Sets_all_four_fields_and_they_round_trip_through_GetRunByIdAsync()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);
        var finishedAt = DateTimeOffset.UtcNow;

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 214, 46, finishedAt);

        var reloaded = await mongo.GetRunByIdAsync(run.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("complete", reloaded!.RagState);
        Assert.Equal(214, reloaded.RagChunksUpserted);
        Assert.Equal(46, reloaded.RagPagesEnglish);
        Assert.Equal(finishedAt, reloaded.RagIndexedAtUtc);
    }

    [Fact]
    public async Task Old_documents_with_no_Rag_fields_report_null_rather_than_throwing()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);

        var reloaded = await mongo.GetRunByIdAsync(run.Id);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.RagState);
        Assert.Null(reloaded.RagChunksUpserted);
        Assert.Null(reloaded.RagPagesEnglish);
        Assert.Null(reloaded.RagIndexedAtUtc);
    }

    [Fact]
    public async Task Does_not_clobber_a_field_set_by_a_concurrent_crawl_progress_write()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);

        // Simulates the crawl worker writing progress at roughly the same time the RAG webhook
        // fires. UpdateRunAsync's read-modify-ReplaceOneAsync would lose whichever write's
        // in-memory copy was captured first; UpdateRagIndexStatusAsync's $set must not.
        await mongo.UpdateRunAsync(run.Id, r => r.HostProgressJson = "{\"pagesSaved\":12}");

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 30, 12, DateTimeOffset.UtcNow);

        var reloaded = await mongo.GetRunByIdAsync(run.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("{\"pagesSaved\":12}", reloaded!.HostProgressJson);
        Assert.Equal("complete", reloaded.RagState);
    }

    [Fact]
    public async Task A_later_crawl_progress_write_after_indexing_also_survives()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 30, 12, DateTimeOffset.UtcNow);
        await mongo.UpdateRunAsync(run.Id, r => r.Status = "complete");

        var reloaded = await mongo.GetRunByIdAsync(run.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("complete", reloaded!.RagState);
        Assert.Equal(30, reloaded.RagChunksUpserted);
        Assert.Equal("complete", reloaded.Status);
    }

    private MongoGeekCrawlerService CreateMongo() =>
        new(_connectionString, NullLogger<MongoGeekCrawlerService>.Instance);

    private static Task<GeekCrawlerRun> SeedRunAsync(IMongoGeekCrawlerService mongo) =>
        mongo.CreateRunAsync(new GeekCrawlerRun
        {
            OwnerUserId = $"rag-status-{Guid.NewGuid():N}",
            CrawlType = CrawlTypes.ProjectSite,
            Status = "pending",
            SeedUrlsJson = "[\"https://example.test/\"]",
            SeedKey = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
}
