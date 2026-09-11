using System.Net;
using System.Text;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2TaskAgentPageHydratorTests
{
    private static readonly IPAddress PublicExample = IPAddress.Parse("93.184.216.34");
    private static GccV2SafeOutboundUrl.HostResolver PublicOnly => _ => [PublicExample];

    [Fact]
    public async Task Rejects_ssrf_targets_without_http()
    {
        using var http = new HttpClient(new RecordingHandler());
        var hydrator = new GccV2TaskAgentPageHydrator(http);

        var outcome = await hydrator.HydrateAsync("http://127.0.0.1/", CancellationToken.None, PublicOnly);

        Assert.False(outcome.Ok);
        Assert.Equal("ssrf", outcome.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, outcome.HttpStatus);
    }

    [Fact]
    public async Task Rejects_redirect_into_private_host()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var hydrator = new GccV2TaskAgentPageHydrator(http);

        var outcome = await hydrator.HydrateAsync("https://example.com/start", CancellationToken.None, PublicOnly);

        Assert.False(outcome.Ok);
        Assert.Equal("ssrf", outcome.ErrorCode);
        Assert.Contains("not allowed", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Happy_path_extracts_visible_markdown_and_technical_fields()
    {
        const string html = """
            <html><head><title>AI Readiness Guide</title></head>
            <body>
              <h1>AI Readiness Guide</h1>
              <p>This page explains how teams measure content readiness with concrete evidence and clear answers for readers who need actionable checks.</p>
              <h2>Checklist</h2>
              <p>Operators should verify crawlability, schema coverage, and factual density before publishing AI-facing pages into production surfaces.</p>
            </body></html>
            """;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
            }));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var hydrator = new GccV2TaskAgentPageHydrator(http);

        var outcome = await hydrator.HydrateAsync("https://example.com/guide", CancellationToken.None, PublicOnly);

        Assert.True(outcome.Ok);
        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal("yes", outcome.Crawlable == true ? "yes" : "no");
        Assert.Equal("full", outcome.ContentCompleteness);
        Assert.Contains("AI Readiness Guide", outcome.VisibleContent);
        Assert.Contains("Checklist", outcome.VisibleContent);
        Assert.Contains("factual density", outcome.VisibleContent);
        Assert.True(outcome.LoadTimeMs is > 0);
    }

    [Fact]
    public void FormatVisibleContent_builds_heading_markdown()
    {
        var page = new GccQuoteablePage(
            "https://example.com/a",
            "Title",
            [new HeadingDto(2, "Section")],
            ["A paragraph with enough characters to survive extractor floors."]);

        var text = GccV2TaskAgentPageHydrator.FormatVisibleContent(page);

        Assert.Contains("# Title", text);
        Assert.Contains("## Section", text);
        Assert.Contains("enough characters", text);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responder;

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responder is null)
                throw new InvalidOperationException("HTTP should not have been called.");
            return _responder(request, cancellationToken);
        }
    }
}
