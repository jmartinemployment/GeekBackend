using System.Net;
using GeekAPI.HttpClients;
using GeekAPI.Services;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void FromConfiguration_ignores_seeds_only_outside_development()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_SEEDS_ONLY"] = "true",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(
            config,
            new FakeHostEnvironment { EnvironmentName = Environments.Production });

        Assert.False(options.SeedsOnly);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GeekBackend.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
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
        services.AddSingleton<GeekCrawlerRunCoordinator>();

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

    [Fact]
    public void DescribeZeroHtmlFailure_distinguishes_status_zero_from_empty_2xx()
    {
        var allZero = new Dictionary<string, OriginProgressStats>
        {
            ["https://a.com"] = new(),
        };
        allZero["https://a.com"].AddPage(0, hasHtml: false, failureReason: "TimeoutException");

        var empty2xx = new Dictionary<string, OriginProgressStats>
        {
            ["https://b.com"] = new(),
        };
        empty2xx["https://b.com"].AddPage(200, hasHtml: false);

        Assert.Contains("status 0", GeekCrawlerHostProgress.DescribeZeroHtmlFailure(allZero));
        Assert.Contains("empty", GeekCrawlerHostProgress.DescribeZeroHtmlFailure(empty2xx));
        Assert.Contains("2xx", GeekCrawlerHostProgress.DescribeZeroHtmlFailure(empty2xx));
    }
}

public class GeekCrawlerSeedOriginGroupingTests
{
    [Fact]
    public void GroupSeedsByOrigin_merges_www_and_bare_domain()
    {
        var grouped = GeekCrawlerSeedNormalizer.GroupSeedsByOrigin(
        [
            "https://www.example.com/page",
            "https://example.com/other",
        ]);

        Assert.Single(grouped);
        Assert.Equal(2, grouped.First().Value.Count);
    }

    [Fact]
    public void NormalizeOriginAuthority_strips_www_prefix()
    {
        Assert.Equal(
            "https://example.com",
            GeekCrawlerSeedNormalizer.NormalizeOriginAuthority("https://www.example.com"));
    }
}

public class GeekCrawlerHomepageUrlTests
{
    [Fact]
    public void TryNormalize_returns_origin_homepage()
    {
        Assert.True(GeekCrawlerHomepageUrl.TryNormalize("example.com/pricing", out var url));
        Assert.Equal("https://example.com/", url);
    }
}

public class GeekCrawlerPoliteGateRobotsTests
{
    [Fact]
    public void IsUrlAllowed_returns_false_when_robots_forbidden()
    {
        var registry = new GeekCrawlerHostRegistry(GeekCrawlerOptions.FromConfiguration(
            new ConfigurationBuilder().Build()));
        registry.SetRobotsForbidden("https://blocked.com");

        var gate = new GeekCrawlerPoliteGate(
            new HttpClient(),
            registry,
            TimeProvider.System,
            GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            NullLogger<GeekCrawlerPoliteGate>.Instance);

        var url = new Uri("https://blocked.com/page");
        Assert.False(gate.IsUrlAllowed(url));
    }

    [Fact]
    public async Task EnsureRobots_http_403_defaults_to_allow_not_block_all()
    {
        var handler = new StubHttpHandler(req =>
        {
            Assert.EndsWith("/robots.txt", req.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("forbidden"),
            };
        });
        var registry = new GeekCrawlerHostRegistry(GeekCrawlerOptions.FromConfiguration(
            new ConfigurationBuilder().Build()));
        var gate = new GeekCrawlerPoliteGate(
            new HttpClient(handler),
            registry,
            TimeProvider.System,
            GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            NullLogger<GeekCrawlerPoliteGate>.Instance);

        await gate.EnsureRobotsForOriginAsync("https://botpenguin.com", CancellationToken.None);

        Assert.False(registry.IsRobotsForbidden("https://botpenguin.com"));
        Assert.True(gate.IsUrlAllowed(new Uri("https://botpenguin.com/")));
        var prepared = await gate.PrepareFetchAsync(new Uri("https://botpenguin.com/"), CancellationToken.None);
        Assert.True(prepared.Allowed);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}

public class GeekCrawlerRunCoordinatorTests
{
    [Fact]
    public void Register_replaces_prior_token_and_cancels_old()
    {
        var coordinator = new GeekCrawlerRunCoordinator();
        var runId = Guid.NewGuid();
        var first = coordinator.Register(runId);
        var second = coordinator.Register(runId);

        Assert.NotEqual(default, first);
        Assert.NotEqual(default, second);
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_cancels_registered_token()
    {
        var coordinator = new GeekCrawlerRunCoordinator();
        var runId = Guid.NewGuid();
        var token = coordinator.Register(runId);
        coordinator.Cancel(runId);
        Assert.True(token.IsCancellationRequested);
    }
}

public class GeekCrawlerStallRecoveryTests
{
    [Fact]
    public void ScanInterval_is_two_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(2), GeekCrawlerStallRecoveryHostedService.ScanInterval);

    [Fact]
    public void ShouldLogAndContinue_true_for_HttpClient_timeout_when_host_not_stopping()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException (an OCE) with a non-cancelled token.
        using var cts = new CancellationTokenSource();
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        Assert.True(HostedServiceScan.ShouldLogAndContinue(timeout, cts.Token));
    }

    [Fact]
    public void ShouldLogAndContinue_false_when_host_stopping_cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = new OperationCanceledException(cts.Token);

        Assert.False(HostedServiceScan.ShouldLogAndContinue(canceled, cts.Token));
    }

    [Fact]
    public void ShouldLogAndContinue_true_for_ordinary_exceptions()
    {
        using var cts = new CancellationTokenSource();
        Assert.True(HostedServiceScan.ShouldLogAndContinue(new InvalidOperationException("boom"), cts.Token));
    }
}

public class GeekCrawlerCapsTests
{
    [Fact]
    public void NavigationTimeout_is_30_seconds() =>
        Assert.Equal(30_000, GeekCrawlerCaps.NavigationTimeoutMs);
}

public class GeekCrawlerSitemapSeederTests
{
    [Fact]
    public async Task CollectAllowedUrlsAsync_parses_urlset_and_filters_robots()
    {
        const string sitemapXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://example.com/</loc></url>
              <url><loc>https://example.com/pricing</loc></url>
              <url><loc>https://other.com/page</loc></url>
            </urlset>
            """;

        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sitemapXml),
        });
        var http = new HttpClient(handler);
        var registry = new GeekCrawlerHostRegistry(GeekCrawlerOptions.FromConfiguration(
            new ConfigurationBuilder().Build()));
        var gate = new GeekCrawlerPoliteGate(
            new HttpClient(),
            registry,
            TimeProvider.System,
            GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            NullLogger<GeekCrawlerPoliteGate>.Instance);
        var seeder = new GeekCrawlerSitemapSeeder(
            http,
            gate,
            NullLogger<GeekCrawlerSitemapSeeder>.Instance);

        var urls = await seeder.CollectAllowedUrlsAsync("https://example.com", CancellationToken.None);

        Assert.Equal(2, urls.Count);
        Assert.Contains(urls, u => u.Contains("/pricing", StringComparison.Ordinal));
        Assert.DoesNotContain(urls, u => u.Contains("other.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectAllowedUrlsAsync_truncates_at_MaxSitemapUrlsPerOrigin()
    {
        var urlEntries = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, GeekCrawlerCaps.MaxSitemapUrlsPerOrigin + 2)
                .Select(i => $"<url><loc>https://example.com/page-{i}</loc></url>"));
        var sitemapXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
            {urlEntries}
            </urlset>
            """;

        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/robots.txt", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nAllow: /\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sitemapXml),
            };
        });
        var http = new HttpClient(handler);
        var registry = new GeekCrawlerHostRegistry(GeekCrawlerOptions.FromConfiguration(
            new ConfigurationBuilder().Build()));
        var gate = new GeekCrawlerPoliteGate(
            http,
            registry,
            TimeProvider.System,
            GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            NullLogger<GeekCrawlerPoliteGate>.Instance);
        var seeder = new GeekCrawlerSitemapSeeder(
            http,
            gate,
            NullLogger<GeekCrawlerSitemapSeeder>.Instance);

        var urls = await seeder.CollectAllowedUrlsAsync("https://example.com", CancellationToken.None);

        Assert.Equal(GeekCrawlerCaps.MaxSitemapUrlsPerOrigin, urls.Count);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}

public class GeekCrawlerPlaywrightIntegrationTests
{
    [Fact]
    public async Task Mobile_fetch_returns_html_when_integration_enabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_PLAYWRIGHT_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var holder = new GeekCrawlerPlaywrightHolder();
        await holder.InitializeAsync();
        Assert.NotNull(holder.Browser);

        var registry = new GeekCrawlerHostRegistry(GeekCrawlerOptions.FromConfiguration(
            new ConfigurationBuilder().Build()));
        var gate = new GeekCrawlerPoliteGate(
            new HttpClient(),
            registry,
            TimeProvider.System,
            GeekCrawlerOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            NullLogger<GeekCrawlerPoliteGate>.Instance);
        var fetcher = new MobilePageFetcher(
            holder,
            gate,
            registry,
            NullLogger<MobilePageFetcher>.Instance);

        var result = await fetcher.FetchAsync("https://example.com/", CancellationToken.None);

        Assert.NotNull(result.Html);
        Assert.True(result.StatusCode is >= 200 and < 300);
    }
}

public class GeekCrawlerScheduleTests
{
    [Fact]
    public void ScheduleScanInterval_is_one_minute() =>
        Assert.Equal(TimeSpan.FromMinutes(1), GeekCrawlerScheduleHostedService.ScanInterval);
}

public class GeekCrawlerUrlKeysTests
{
    [Fact]
    public void CrawlKey_includes_query_string()
    {
        var withQuery = GeekCrawlerUrlKeys.CrawlKey("https://example.com/pricing?tab=annual");
        var withoutQuery = GeekCrawlerUrlKeys.CrawlKey("https://example.com/pricing");
        Assert.NotEqual(withQuery, withoutQuery);
    }
}

public class GeekCrawlerChallengeDetectorTests
{
    [Fact]
    public void IsCloudflareChallenge_detects_challenge_html()
    {
        const string html = """
            <html><head><title>Just a moment...</title></head>
            <body><div id="challenge-platform">Checking your browser</div></body></html>
            """;
        Assert.True(GeekCrawlerChallengeDetector.IsCloudflareChallenge(403, html));
    }

    [Fact]
    public void IsCloudflareChallenge_ignores_normal_page()
    {
        const string html = "<html><body><h1>Hello</h1></body></html>";
        Assert.False(GeekCrawlerChallengeDetector.IsCloudflareChallenge(200, html));
    }
}
