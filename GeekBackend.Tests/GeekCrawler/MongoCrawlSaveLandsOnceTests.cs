using EphemeralMongo;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// A save the crawler sends twice lands once.
///
/// Since 2026-10-24 the crawler sends a page or link save that failed in transit once more
/// (Geek-Crawler-v2 geek-api-client.ts). The first attempt may have reached Mongo and lost only its
/// acknowledgment, so the second arrival of the same page under the same run must not become a
/// second document, and the Id the crawler is told must be the page that exists. Links arrive whole
/// per page and are replaced, never added to. Jeff, 2026-10-24: a power loss of about a second on
/// 2026-10-23 cost five crawls one lost save each.
/// </summary>
public sealed class MongoCrawlSaveLandsOnceTests : IAsyncLifetime
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
    public async Task A_page_sent_twice_is_stored_once_and_answers_the_same_id()
    {
        var mongo = CreateMongo();
        var runId = Guid.NewGuid();

        var first = await mongo.CreatePagesBatchAsync(runId, [NewPage(runId, "https://retry.example/a", "First")]);
        // The same save again, as the crawler sends it after a lost acknowledgment: a new document
        // object with a new Id and the same URL.
        var second = await mongo.CreatePagesBatchAsync(runId, [NewPage(runId, "https://retry.example/a", "Second")]);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].PageId, second[0].PageId);
        Assert.Equal("https://retry.example/a", second[0].Url);
        Assert.Equal(1, await mongo.CountPagesByRunAsync(runId));

        // The first write is what is kept; the repeat changes nothing.
        var stored = Assert.Single(await mongo.ListPagesByRunAsync(runId, 10, 0));
        Assert.Equal(first[0].PageId, stored.Id);
        Assert.Equal("First", stored.Title);
    }

    [Fact]
    public async Task A_second_url_in_the_same_run_is_a_second_page()
    {
        var mongo = CreateMongo();
        var runId = Guid.NewGuid();

        var a = await mongo.CreatePagesBatchAsync(runId, [NewPage(runId, "https://retry.example/a", "A")]);
        var b = await mongo.CreatePagesBatchAsync(runId, [NewPage(runId, "https://retry.example/b", "B")]);

        Assert.NotEqual(a[0].PageId, b[0].PageId);
        Assert.Equal(2, await mongo.CountPagesByRunAsync(runId));
    }

    [Fact]
    public async Task The_same_url_in_another_run_is_that_runs_own_page()
    {
        var mongo = CreateMongo();
        var runOne = Guid.NewGuid();
        var runTwo = Guid.NewGuid();

        var one = await mongo.CreatePagesBatchAsync(runOne, [NewPage(runOne, "https://retry.example/a", "Run one")]);
        var two = await mongo.CreatePagesBatchAsync(runTwo, [NewPage(runTwo, "https://retry.example/a", "Run two")]);

        Assert.NotEqual(one[0].PageId, two[0].PageId);
        Assert.Equal(1, await mongo.CountPagesByRunAsync(runOne));
        Assert.Equal(1, await mongo.CountPagesByRunAsync(runTwo));
    }

    [Fact]
    public async Task A_batch_of_several_pages_answers_each_in_order_with_repeats_resolved()
    {
        var mongo = CreateMongo();
        var runId = Guid.NewGuid();

        var first = await mongo.CreatePagesBatchAsync(runId, [NewPage(runId, "https://retry.example/a", "A")]);
        var batch = await mongo.CreatePagesBatchAsync(runId,
        [
            NewPage(runId, "https://retry.example/a", "A again"),
            NewPage(runId, "https://retry.example/b", "B"),
        ]);

        Assert.Equal(2, batch.Count);
        Assert.Equal("https://retry.example/a", batch[0].Url);
        Assert.Equal(first[0].PageId, batch[0].PageId);
        Assert.Equal("https://retry.example/b", batch[1].Url);
        Assert.NotEqual(first[0].PageId, batch[1].PageId);
        Assert.Equal(2, await mongo.CountPagesByRunAsync(runId));
    }

    [Fact]
    public async Task A_pages_links_sent_twice_are_stored_once()
    {
        var mongo = CreateMongo();
        var runId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var links = new List<(Guid PageId, string FromUrl, string LinkUrl, bool IsSameOrigin)>
        {
            (pageId, "https://retry.example/a", "https://retry.example/b", true),
            (pageId, "https://retry.example/a", "https://elsewhere.example/", false),
        };

        Assert.Equal(2, await mongo.WritePageLinksAsync(runId, links));
        Assert.Equal(2, await mongo.WritePageLinksAsync(runId, links));

        var stored = await mongo.ListLinksByRunAsync(runId, null, 100, 0);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, l => Assert.Equal(pageId, l.PageId));
        Assert.Equal(
            new[] { "https://elsewhere.example/", "https://retry.example/b" },
            stored.Select(l => l.LinkUrl).OrderBy(u => u, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Another_pages_links_in_the_same_run_are_left_alone()
    {
        var mongo = CreateMongo();
        var runId = Guid.NewGuid();
        var pageA = Guid.NewGuid();
        var pageB = Guid.NewGuid();

        await mongo.WritePageLinksAsync(runId, [(pageA, "https://retry.example/a", "https://retry.example/x", true)]);
        await mongo.WritePageLinksAsync(runId, [(pageB, "https://retry.example/b", "https://retry.example/y", true)]);
        // Page A's batch again, with a different set: replaced, and B is untouched.
        await mongo.WritePageLinksAsync(runId, [(pageA, "https://retry.example/a", "https://retry.example/z", true)]);

        var stored = await mongo.ListLinksByRunAsync(runId, null, 100, 0);
        Assert.Equal(2, stored.Count);
        Assert.Equal("https://retry.example/z", Assert.Single(stored, l => l.PageId == pageA).LinkUrl);
        Assert.Equal("https://retry.example/y", Assert.Single(stored, l => l.PageId == pageB).LinkUrl);
    }

    private static GeekCrawlerPage NewPage(Guid runId, string url, string title) => new()
    {
        RunId = runId,
        Origin = "https://retry.example",
        Url = url,
        FinalUrl = url,
        StatusCode = 200,
        RobotsAllowed = true,
        Title = title,
        ContentHtml = $"<p>{title}</p>",
        CrawledAtUtc = DateTimeOffset.UtcNow,
    };

    private MongoGeekCrawlerService CreateMongo() =>
        new(_connectionString, NullLogger<MongoGeekCrawlerService>.Instance);
}
