using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Services.JsonLd;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Stage 8a: competitor heading outlines and declared schema, from already-persisted crawl pages
/// only -- no new crawl, no new fetch. Every assertion here is about what got extracted, not about
/// outline selection (that's the deferred Stage 2 / Coverage Gate, a separate, later consumer).
/// </summary>
public class GccCompetitorAnalysisResolverTests
{
    private static GccProjectDto Project(params string[] competitorUrls) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        Name: "Acme",
        Code: null,
        Description: null,
        Status: "active",
        SiteUrl: "https://acme.test",
        ProjectSiteRunId: null,
        Department: "marketing",
        PartnerUrls: [],
        CompetitorUrls: competitorUrls,
        StartDate: new DateOnly(2026, 1, 1),
        DueDate: null,
        FinishedDate: null,
        EstimatedHours: null,
        Budget: null,
        BudgetCurrency: null,
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);

    private static GeekCrawlerPageDto CrawledPage(string url, string html) => new(
        Id: Guid.NewGuid(),
        RunId: Guid.NewGuid(),
        Origin: url,
        Url: url,
        FinalUrl: url,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: html,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow);

    private sealed class FakeProjects(GccProjectDto? project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(project);
    }

    private sealed class FakePages(IReadOnlyList<GeekCrawlerPageDto>? pages = null) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult(pages ?? []);
    }

    private sealed class FakeRag(IReadOnlyList<GeekCrawlerRagHostIndex>? hosts = null) : IGeekCrawlerRagClient
    {
        public bool IsEnabled => true;
        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, CancellationToken ct = default) =>
            Task.FromResult(hosts ?? []);
        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagQueryResult?>(null);
        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateIndexResult?>(null);
        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateQueryResult?>(null);
        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            Task.FromResult<GeekCrawlerRagPageText?>(null);
        public Task<System.Text.Json.JsonElement?> RunDiagnosticAsync(
            string endpoint, object? payload = null, CancellationToken ct = default) =>
            Task.FromResult<System.Text.Json.JsonElement?>(null);
        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            Task.FromResult(new GeekCrawlerRagCapabilities());
    }

    private static GccCompetitorAnalysisResolver Build(
        IGccProjectReader projects, IGccCrawlPageReader pages, IGeekCrawlerRagClient rag) =>
        new(projects, pages, rag, new JsonLdParserService(), NullLogger<GccCompetitorAnalysisResolver>.Instance);

    [Fact]
    public async Task NoProjectYieldsNoAnalysis()
    {
        var resolver = Build(new FakeProjects(null), new FakePages(), new FakeRag());

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task AProjectWithNoCompetitorUrlsYieldsNoAnalysisWithoutCallingAnything()
    {
        var resolver = Build(new FakeProjects(Project()), new FakePages(), new FakeRag());

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task UnindexedCompetitorUrlsYieldNoAnalysisRatherThanThrowing()
    {
        // Not an error: a competitor that hasn't been crawled yet is a legitimate, common state.
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", false, null)]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), new FakePages(), rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task APageWithNoHtmlIsSkippedNotFailed()
    {
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([CrawledPage("https://c.test/a", "")]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractsTheHeadingTreeFromAPageWithRealHtml()
    {
        const string html = """
            <html><body>
              <h1>Pricing</h1>
              <p>Overview text.</p>
              <h2>Enterprise Plan</h2>
              <p>Details for enterprise.</p>
            </body></html>
            """;
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([CrawledPage("https://c.test/pricing", html)]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        var page = Assert.Single(result);
        Assert.Equal("https://c.test/pricing", page.Url);
        // A tree, not a flat list: the h2 nests under the h1 as its child, not as a sibling.
        var h1 = Assert.Single(page.Headings);
        Assert.Equal("Pricing", h1.HeadingText);
        Assert.Equal(1, h1.Level);
        var h2 = Assert.Single(h1.Children);
        Assert.Equal("Enterprise Plan", h2.HeadingText);
        Assert.Equal(2, h2.Level);
    }

    [Fact]
    public async Task ExtractsDeclaredSchemaTypesFromAPageWithJsonLd()
    {
        const string html = """
            <html><body>
              <h1>Widgets</h1>
              <script type="application/ld+json">
              {"@context":"https://schema.org","@type":"Product","name":"Widget Pro"}
              </script>
            </body></html>
            """;
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([CrawledPage("https://c.test/widget", html)]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        var page = Assert.Single(result);
        Assert.Contains("Product", page.DeclaredSchemaTypes);
    }

    [Fact]
    public async Task APageWithNoJsonLdHasEmptyDeclaredTypesNotAThrow()
    {
        const string html = "<html><body><h1>No schema here</h1></body></html>";
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([CrawledPage("https://c.test/plain", html)]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Empty(Assert.Single(result).DeclaredSchemaTypes);
    }
}
