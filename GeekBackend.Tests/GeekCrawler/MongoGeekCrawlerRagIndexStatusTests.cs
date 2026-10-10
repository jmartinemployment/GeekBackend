using EphemeralMongo;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.GeekCrawler;

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

    /// <summary>
    /// The write reports whether it did anything.
    ///
    /// <para>
    /// There is no upsert on the <c>$set</c>, so a runId with no document matches nothing and raises
    /// nothing. The webhook receiver answered <c>202 Accepted</c> for exactly that, telling the
    /// Library its chunk and page counts had been recorded when they had been dropped — and those
    /// three numbers are what the declared-URL evidence gate reads.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reports_true_when_a_run_matched_and_false_when_none_did()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);

        Assert.True(
            await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 214, 46, 460, DateTimeOffset.UtcNow),
            "a run that exists must report the write as done");

        Assert.False(
            await mongo.UpdateRagIndexStatusAsync(
                Guid.NewGuid(), "complete", 214, 46, 460, DateTimeOffset.UtcNow),
            "a runId with no document wrote nothing and must not report otherwise");
    }

    /// <summary>
    /// A purged run is the realistic way the false case arrives: the crawler deletes a failed run
    /// from GeekAPI while the Library still holds an index job for it.
    /// </summary>
    [Fact]
    public async Task Reports_false_once_the_run_is_gone()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);

        Assert.True(await mongo.UpdateRagIndexStatusAsync(
            run.Id, "running", 100, 20, 0, null));

        await mongo.DeleteRunAsync(run.Id);

        Assert.False(
            await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 214, 46, 460, DateTimeOffset.UtcNow),
            "the run is gone, so the terminal status went nowhere and must be reported as such");
    }

    [Fact]
    public async Task Sets_all_five_fields_and_they_round_trip_through_GetRunByIdAsync()
    {
        var mongo = CreateMongo();
        var run = await SeedRunAsync(mongo);
        var finishedAt = DateTimeOffset.UtcNow;

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 214, 46, 460, finishedAt);

        var reloaded = await mongo.GetRunByIdAsync(run.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("complete", reloaded!.RagState);
        Assert.Equal(214, reloaded.RagChunksUpserted);
        Assert.Equal(46, reloaded.RagPagesEnglish);
        Assert.Equal(460, reloaded.RagPagesSkippedUnusable);
        Assert.Equal(finishedAt, reloaded.RagIndexedAtUtc);
    }

    /// <summary>
    /// 46 English pages out of 506 is either a small site or a gutted crawl, and until
    /// 2026-09-29 nothing persisted told them apart: the RAG sent pagesSkippedUnusable on every
    /// webhook, GeekAPI's DTO did not bind it, and the SignalR frame that carried its two benign
    /// siblings -- skippedLang, skippedEmpty, both 0 here -- was live-only anyway.
    /// </summary>
    [Fact]
    public async Task The_unusable_count_distinguishes_a_small_site_from_a_gutted_crawl()
    {
        var mongo = CreateMongo();
        var small = await SeedRunAsync(mongo);
        var gutted = await SeedRunAsync(mongo);

        await mongo.UpdateRagIndexStatusAsync(small.Id, "complete", 214, 46, 0, DateTimeOffset.UtcNow);
        await mongo.UpdateRagIndexStatusAsync(gutted.Id, "complete", 214, 46, 460, DateTimeOffset.UtcNow);

        var a = await mongo.GetRunByIdAsync(small.Id);
        var b = await mongo.GetRunByIdAsync(gutted.Id);

        // Identical on every other Rag field. Only the new one separates them.
        Assert.Equal(a!.RagPagesEnglish, b!.RagPagesEnglish);
        Assert.Equal(a.RagChunksUpserted, b.RagChunksUpserted);
        Assert.Equal(0, a.RagPagesSkippedUnusable);
        Assert.Equal(460, b.RagPagesSkippedUnusable);
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
        Assert.Null(reloaded.RagPagesSkippedUnusable);
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

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 30, 12, 3, DateTimeOffset.UtcNow);

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

        await mongo.UpdateRagIndexStatusAsync(run.Id, "complete", 30, 12, 3, DateTimeOffset.UtcNow);
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
