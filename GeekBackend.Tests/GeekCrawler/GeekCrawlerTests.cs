using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.Tests.GeekCrawler;

public class GeekCrawlerSeedNormalizerTests
{
    [Fact]
    public void NormalizeSeeds_adds_https_for_bare_domain()
    {
        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds(["example.com", "https://b.com/"]);
        Assert.Equal(2, seeds.Count);
        Assert.Contains(seeds, s => s.Equals("https://example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(seeds, s => s.Contains("b.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SeedUrlsMatch_requires_same_url_set()
    {
        const string stored = """["https://a.com","https://b.com"]""";
        Assert.True(GeekCrawlerSeedNormalizer.SeedUrlsMatch(
            stored,
            ["https://b.com", "https://a.com"]));
        Assert.False(GeekCrawlerSeedNormalizer.SeedUrlsMatch(
            stored,
            ["https://a.com"]));
    }

    [Fact]
    public void GroupSeedsByOrigin_groups_same_host()
    {
        var grouped = GeekCrawlerSeedNormalizer.GroupSeedsByOrigin(
        [
            "https://pipedrive.com/",
            "https://pipedrive.com/pricing",
        ]);

        Assert.Single(grouped);
        Assert.Equal(2, grouped.First().Value.Count);
    }
}

public class GeekCrawlerStartRulesTests
{
    [Fact]
    public void CrawlTypes_rejects_unknown_type()
    {
        Assert.False(CrawlTypes.IsValid("vendor"));
        Assert.True(CrawlTypes.IsValid(CrawlTypes.Partner));
    }

    [Fact]
    public void ShouldWakeAtStartup_skips_recent_pending()
    {
        var recent = new GeekAPI.HttpClients.GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            CrawlTypes.Competitors,
            "pending",
            "[]",
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);

        Assert.False(GeekCrawlerRecovery.ShouldWakeAtStartup(recent, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ShouldRecoverRunningOrphan_detects_deploy_killed_run()
    {
        var now = DateTimeOffset.UtcNow;
        var orphan = new GeekAPI.HttpClients.GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            CrawlTypes.Partner,
            "running",
            """["https://www.pipedrive.com"]""",
            null,
            null,
            null,
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            null);

        Assert.True(GeekCrawlerRecovery.ShouldRecoverRunningOrphan(orphan, now, hasSavedPages: false));
        Assert.False(GeekCrawlerRecovery.ShouldRecoverRunningOrphan(orphan, now, hasSavedPages: true));
    }

    [Fact]
    public void ShouldRecoverRunningOrphan_skips_recent_running()
    {
        var now = DateTimeOffset.UtcNow;
        var recent = new GeekAPI.HttpClients.GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            CrawlTypes.Partner,
            "running",
            "[]",
            null,
            null,
            null,
            now,
            now,
            null);

        Assert.False(GeekCrawlerRecovery.ShouldRecoverRunningOrphan(recent, now, hasSavedPages: false));
    }

    [Fact]
    public void ShouldRecoverStalledRunning_detects_deploy_killed_mid_crawl()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new GeekAPI.HttpClients.GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            CrawlTypes.Partner,
            "running",
            """["https://botpenguin.com"]""",
            null,
            null,
            null,
            now.AddHours(-2),
            now.AddHours(-2),
            null);

        Assert.True(GeekCrawlerRecovery.ShouldRecoverStalledRunning(
            run,
            now,
            now.AddMinutes(-10)));
        Assert.False(GeekCrawlerRecovery.ShouldRecoverStalledRunning(
            run,
            now,
            now.AddMinutes(-1)));
    }
}

public class GeekCrawlerLinkExtractorTests
{
    [Fact]
    public void ExtractAllLinks_includes_external_and_same_origin()
    {
        const string html = """
            <html><body>
              <a href="/pricing">Pricing</a>
              <a href="https://wikipedia.org/wiki/Test">Wiki</a>
              <a href="mailto:a@b.com">Mail</a>
            </body></html>
            """;

        var links = GeekCrawlerLinkExtractor.ExtractAllLinks(
            html,
            "https://example.com/page",
            "https://example.com");

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.IsSameOrigin && l.LinkUrl.Contains("example.com/pricing"));
        Assert.Contains(links, l => !l.IsSameOrigin && l.LinkUrl.Contains("wikipedia.org"));
    }
}

public class GeekCrawlerOptionsTests
{
    [Fact]
    public void FromConfiguration_applies_accelerated_presets()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_MODE"] = "accelerated",
                ["GEEK_CRAWLER_WORKER_COUNT"] = "2",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal("accelerated", options.Mode);
        Assert.Equal(2, options.WorkerCount);
        Assert.Equal(4, options.ParallelismPerOrigin);
        Assert.Equal(3, options.HostDelaySeconds);
    }

    [Fact]
    public void FromConfiguration_worker_count_has_no_upper_cap()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_WORKER_COUNT"] = "10",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal(10, options.WorkerCount);
    }

    [Fact]
    public void FromConfiguration_parses_seeds_only()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_SEEDS_ONLY"] = "true",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.True(options.SeedsOnly);
    }

    [Fact]
    public void FromConfiguration_respects_explicit_overrides()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_MODE"] = "standard",
                ["GEEK_CRAWLER_PARALLELISM_PER_ORIGIN"] = "3",
                ["GEEK_CRAWLER_HOST_DELAY_SECONDS"] = "5",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal(3, options.ParallelismPerOrigin);
        Assert.Equal(5, options.HostDelaySeconds);
        Assert.Equal(5, options.BatchSaveSize);
    }

    [Fact]
    public void FromConfiguration_parses_batch_save_size()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_BATCH_SAVE_SIZE"] = "10",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal(10, options.BatchSaveSize);
    }
}

public class GeekCrawlerWorkerRegistrationTests
{
    [Fact]
    public void RegisterWorkers_registers_one_hosted_service_per_worker()
    {
        const int workerCount = 3;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_WORKER_COUNT"] = workerCount.ToString(),
            })
            .Build()));
        services.AddSingleton<GeekCrawlerWake>();

        GeekCrawlerServiceRegistration.RegisterWorkers(services, workerCount);

        var provider = services.BuildServiceProvider();
        var workers = provider.GetServices<IHostedService>().OfType<GeekCrawlerWorker>().ToList();

        Assert.Equal(workerCount, workers.Count);
    }
}

public class GeekCrawlerHostTrafficControllerTests
{
    [Fact]
    public async Task ExecutePolitelyAsync_allows_parallelism_up_to_max()
    {
        var controller = new GeekCrawlerHostTrafficController(maxParallel: 2);
        var clock = TimeProvider.System;
        var inFlight = 0;
        var maxSeen = 0;

        async Task Work()
        {
            await controller.ExecutePolitelyAsync(async () =>
            {
                var current = Interlocked.Increment(ref inFlight);
                try
                {
                    var peak = current;
                    var prior = maxSeen;
                    while (peak > prior)
                    {
                        prior = Interlocked.CompareExchange(ref maxSeen, peak, prior);
                    }

                    await Task.Delay(25);
                    return 0;
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }, CancellationToken.None);
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Work()));

        Assert.Equal(2, maxSeen);
        _ = clock;
    }
}

public class GeekCrawlerSeedUrlDebugTests
{
    [Theory]
    [InlineData("https://webpalmbeaches.com/ai-consulting-delray-beach.html")]
    [InlineData("* https://webpalmbeaches.com/ai-consulting-delray-beach.html")]
    public void NormalizeSeeds_handles_palm_beaches_url(string raw)
    {
        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds([raw]);
        Assert.NotEmpty(seeds);
    }

    [Fact]
    public void ComputeSeedKey_is_stable_across_seed_order()
    {
        var a = GeekCrawlerSeedNormalizer.ComputeSeedKey(["https://a.com", "https://b.com"]);
        var b = GeekCrawlerSeedNormalizer.ComputeSeedKey(["https://b.com", "https://a.com"]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TryNormalizeSeedUrl_rejects_host_without_dot()
    {
        Assert.False(GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl("https://activecampaign", out _));
        Assert.True(GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl("https://www.activecampaign.com", out _));
        Assert.True(GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl("http://localhost", out _));
    }

    [Fact]
    public void ValidateRawSeeds_returns_error_for_invalid_host()
    {
        var error = GeekCrawlerSeedNormalizer.ValidateRawSeeds(["https://activecampaign"]);
        Assert.NotNull(error);
    }
}

public class GeekCrawlerRunResumeLoaderTests
{
    [Fact]
    public async Task LoadAsync_builds_seen_and_queue_from_resume_rows_without_html()
    {
        var runId = Guid.NewGuid();
        var repo = new FakeResumeRepo(runId);
        repo.AddPage("https://example.com", "https://example.com/", hasHtml: true);
        repo.AddPage("https://example.com", "https://example.com/about", hasHtml: false);
        repo.AddLink("https://example.com/about", "https://example.com/contact");

        var state = await GeekCrawlerRunResumeLoader.LoadAsync(
            repo,
            runId,
            ["https://example.com"],
            CancellationToken.None);

        Assert.Equal(2, state.OriginStats["https://example.com"].Attempted);
        Assert.Equal(1, state.OriginStats["https://example.com"].WithHtml);
        Assert.Contains("https://example.com/contact", state.OriginResume["https://example.com"].QueueUrls);
    }

    private sealed class FakeResumeRepo(Guid runId) : IGeekCrawlerResumeRepository
    {
        private readonly List<GeekCrawlerPageResumeRowDto> _pages = [];
        private readonly List<GeekCrawlerLinkDto> _links = [];
        private readonly List<GeekCrawlerLinkResumeRowDto> _resumeLinks = [];

        public void AddPage(string origin, string url, bool hasHtml) =>
            _pages.Add(new GeekCrawlerPageResumeRowDto(origin, url, hasHtml));

        public void AddLink(string fromUrl, string linkUrl)
        {
            var id = Guid.NewGuid();
            var discoveredAt = DateTimeOffset.UtcNow;
            _links.Add(new GeekCrawlerLinkDto(
                id,
                runId,
                Guid.NewGuid(),
                fromUrl,
                linkUrl,
                true,
                discoveredAt));
            _resumeLinks.Add(new GeekCrawlerLinkResumeRowDto(linkUrl, discoveredAt, id));
        }

        public Task<IReadOnlyList<GeekCrawlerPageResumeRowDto>> ListPagesForResumeAsync(
            Guid runId,
            int limit = 500,
            int offset = 0,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageResumeRowDto>>(
                _pages.Skip(offset).Take(limit).ToList());

        public Task<IReadOnlyList<GeekCrawlerLinkResumeRowDto>> ListLinksForResumeAsync(
            Guid runId,
            int limit = 500,
            DateTimeOffset? afterDiscoveredAtUtc = null,
            Guid? afterId = null,
            CancellationToken ct = default)
        {
            IEnumerable<GeekCrawlerLinkResumeRowDto> rows = _resumeLinks
                .OrderBy(l => l.DiscoveredAtUtc)
                .ThenBy(l => l.Id);

            if (afterDiscoveredAtUtc is not null && afterId is not null)
            {
                rows = rows.Where(l =>
                    l.DiscoveredAtUtc > afterDiscoveredAtUtc.Value
                    || (l.DiscoveredAtUtc == afterDiscoveredAtUtc.Value && l.Id > afterId.Value));
            }

            return Task.FromResult<IReadOnlyList<GeekCrawlerLinkResumeRowDto>>(
                rows.Take(limit).ToList());
        }

        public Task<IReadOnlyList<GeekCrawlerLinkDto>> ListLinksAsync(
            Guid runId,
            bool? sameOrigin = null,
            int limit = 100,
            int offset = 0,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerLinkDto>>(
                _links.Skip(offset).Take(limit).ToList());
    }
}

public class GeekCrawlerHostProgressTests
{
    [Fact]
    public void AllOriginsHaveZeroHtml_when_no_html_saved()
    {
        var stats = new Dictionary<string, OriginProgressStats>
        {
            ["https://a.com"] = new() { Attempted = 1, WithHtml = 0 },
        };
        Assert.True(GeekCrawlerHostProgress.AllOriginsHaveZeroHtml(stats));
    }

    [Fact]
    public void BuildHostProgress_includes_status_counts()
    {
        var stats = new Dictionary<string, OriginProgressStats>
        {
            ["https://a.com"] = new(),
        };
        stats["https://a.com"].AddPage(200, hasHtml: true);
        stats["https://a.com"].AddPage(0, hasHtml: false);

        var json = System.Text.Json.JsonSerializer.Serialize(
            GeekCrawlerHostProgress.BuildHostProgress(["https://a.com"], stats));

        Assert.Contains("statusCounts", json);
        Assert.Contains("pagesWithoutHtml", json);
    }
}
