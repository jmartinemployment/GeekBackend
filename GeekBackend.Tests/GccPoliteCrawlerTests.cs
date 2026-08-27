using System.Net;
using System.Net.Http;
using GeekAPI.Services.ContentCreator.Polite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests;

public class GccPoliteCrawlerTests
{
    private static readonly TimeSpan TestDelay = TimeSpan.FromMilliseconds(50);

    private static (GccPoliteCrawler Crawler, RecordingHandler Handler, GccPoliteHostRegistry Registry, FakeTimeProvider Clock)
        Create(RecordingHandler handler)
    {
        var registry = new GccPoliteHostRegistry();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        // Advance through Task.Delay(wait, clock) so tests never hang on real wall-clock sleeps.
        clock.AutoAdvanceAmount = TestDelay;
        var http = new HttpClient(handler);
        var crawler = new GccPoliteCrawler(
            http,
            registry,
            clock,
            NullLogger<GccPoliteCrawler>.Instance,
            hostDelayOverride: TestDelay);
        return (crawler, handler, registry, clock);
    }

    [Fact]
    public async Task GetHtmlAsync_Honors_RobotsTxt_Disallow()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nDisallow: /blocked-path\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><h1>Should not fetch</h1></body></html>"),
            };
        });

        var (crawler, _, _, _) = Create(handler);
        var result = await crawler.GetHtmlAsync(new Uri("https://partner-a.com/blocked-path"));

        Assert.Equal(GccPoliteFetchResult.Statuses.BlockedByRobots, result.Status);
        Assert.Null(result.Html);
        Assert.Equal(1, handler.Count(r => r.AbsolutePath == "/robots.txt"));
        Assert.Equal(0, handler.Count(r => r.AbsolutePath == "/blocked-path"));
    }

    [Fact]
    public async Task GetHtmlAsync_SameHost_Sets_NextAllowedTime_After_Success()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nAllow: /\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><title>ok</title><body><p>hi</p></body></html>"),
            };
        });

        var (crawler, _, registry, clock) = Create(handler);
        var before = clock.GetUtcNow();
        var first = await crawler.GetHtmlAsync(new Uri("https://partner-a.com/one"));
        Assert.Equal(GccPoliteFetchResult.Statuses.Success, first.Status);

        var controller = registry.GetController("https://partner-a.com");
        Assert.True(controller.NextAllowedTime >= before + TestDelay);

        var second = await crawler.GetHtmlAsync(new Uri("https://partner-a.com/two"));
        Assert.Equal(GccPoliteFetchResult.Statuses.Success, second.Status);
        Assert.True(handler.Count(r => r.AbsolutePath is "/one" or "/two") >= 2);
    }

    [Fact]
    public async Task GetHtmlAsync_429_Applies_RetryAfter_Without_Throwing()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nAllow: /\n"),
                };
            }

            var msg = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            msg.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return msg;
        });

        var (crawler, _, registry, clock) = Create(handler);
        var before = clock.GetUtcNow();
        var result = await crawler.GetHtmlAsync(new Uri("https://partner-a.com/page"));
        Assert.Equal(GccPoliteFetchResult.Statuses.RateLimited, result.Status);
        Assert.Null(result.Html);

        var next = registry.GetController("https://partner-a.com").NextAllowedTime;
        Assert.True(next >= before + TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task GetHtmlAsync_Robots_FailOpen_Allows_Page()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/robots.txt")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><title>ok</title><body><p>hi</p></body></html>"),
            };
        });

        var (crawler, _, _, _) = Create(handler);
        var result = await crawler.GetHtmlAsync(new Uri("https://partner-b.com/ok"));
        Assert.Equal(GccPoliteFetchResult.Statuses.Success, result.Status);
        Assert.NotNull(result.Html);
    }

    [Fact]
    public async Task GetHtmlAsync_HostB_Does_Not_Wait_On_HostA()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nAllow: /\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><title>ok</title><body><p>hi</p></body></html>"),
            };
        });

        var (crawler, _, registry, clock) = Create(handler);

        var a = await crawler.GetHtmlAsync(new Uri("https://host-a.example/page"));
        Assert.Equal(GccPoliteFetchResult.Statuses.Success, a.Status);

        var bStarted = clock.GetUtcNow();
        var b = await crawler.GetHtmlAsync(new Uri("https://host-b.example/page"));
        Assert.Equal(GccPoliteFetchResult.Statuses.Success, b.Status);

        var aNext = registry.GetController("https://host-a.example").NextAllowedTime;
        Assert.True(aNext > bStarted - TestDelay);
        Assert.Contains(handler.Requests, r => r.Host == "host-b.example" && r.AbsolutePath == "/page");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public List<Uri> Requests { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) =>
            _respond = respond;

        public int Count(Func<Uri, bool> pred) => Requests.Count(pred);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_respond(request, cancellationToken));
        }
    }
}
