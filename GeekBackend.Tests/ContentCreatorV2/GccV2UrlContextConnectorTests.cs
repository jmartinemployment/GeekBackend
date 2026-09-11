using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2UrlContextConnectorTests
{
    [Fact]
    public void Registry_lists_url_connector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        services.AddSingleton<IGccV2ContextConnector, GccV2UrlContextConnector>();
        services.AddSingleton<GccV2ContextConnectorRegistry>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<GccV2ContextConnectorRegistry>();

        Assert.Contains(GccV2UrlContextConnector.ConnectorId, registry.ConnectorIds);
        Assert.Equal(GccV2UrlContextConnector.ConnectorId, registry.Resolve("url").Id);
    }

    [Fact]
    public void CanRefresh_requires_url_descriptor()
    {
        var connector = new GccV2UrlContextConnector(new StubHttpClientFactory());
        using var ok = JsonDocument.Parse("""{"connectorId":"url","url":"https://example.com/a"}""");
        using var missing = JsonDocument.Parse("""{"connectorId":"url"}""");
        using var other = JsonDocument.Parse("""{"connectorId":"drive","url":"https://example.com/a"}""");

        Assert.True(connector.CanRefresh(ok.RootElement));
        Assert.False(connector.CanRefresh(missing.RootElement));
        Assert.False(connector.CanRefresh(other.RootElement));
    }

    [Fact]
    public async Task FetchAsync_rejects_ssrf_targets()
    {
        var connector = new GccV2UrlContextConnector(new StubHttpClientFactory());
        using var descriptor = JsonDocument.Parse("""{"connectorId":"url","url":"http://127.0.0.1/"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.FetchAsync(descriptor.RootElement, "owner", CancellationToken.None));

        Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_happy_path_returns_markdown_revision()
    {
        const string html = """
            <html><head><title>Knowledge Page</title></head>
            <body>
              <h1>Knowledge Page</h1>
              <p>This page explains governed Knowledge ingestion from a public URL with enough characters for extraction.</p>
            </body></html>
            """;
        var factory = new StubHttpClientFactory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
            }));
        var connector = new GccV2UrlContextConnector(factory);
        using var descriptor = JsonDocument.Parse("""{"connectorId":"url","url":"https://example.com/kb"}""");

        var revision = await connector.FetchAsync(descriptor.RootElement, "owner", CancellationToken.None);
        await using var _ = revision.Content;
        using var reader = new StreamReader(revision.Content, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        Assert.Contains("Knowledge Page", text);
        Assert.Equal("https://example.com/kb", revision.SourceUrl);
        Assert.Contains("markdown", revision.MediaType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responder;

        public StubHttpClientFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
        {
            _responder = responder;
        }

        public HttpClient CreateClient(string name)
        {
            HttpMessageHandler handler = _responder is null
                ? new ThrowingHandler()
                : new DelegateHandler(_responder);
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("HTTP should not have been called.");
        }

        private sealed class DelegateHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                responder(request, cancellationToken);
        }
    }
}
