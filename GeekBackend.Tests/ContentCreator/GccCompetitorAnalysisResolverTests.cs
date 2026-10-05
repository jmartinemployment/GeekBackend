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
    internal static GccProjectDto Project(params string[] competitorUrls) => new(
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

    internal static GeekCrawlerPageDto CrawledPage(string url, string html) => new(
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

    internal sealed class FakeProjects(GccProjectDto? project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(project);
    }

    internal sealed class FakePages(IReadOnlyList<GeekCrawlerPageDto>? pages = null) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult(pages ?? []);

        // The resolver reads the whole run now, not the declared seed URLs -- the by-seeds answer above
        // is no longer what it consumes.
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult(offset == 0 ? pages ?? [] : []);
    }

    /// <param name="query">What a search of (run, crawl type) returns. Null answers every search with
    /// null -- the library failing -- as this fake always did.</param>
    internal sealed class FakeRag(
        IReadOnlyList<GeekCrawlerRagHostIndex>? hosts = null,
        Func<Guid, string?, GeekCrawlerRagQueryResult?>? query = null) : IGeekCrawlerRagClient
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
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            Task.FromResult(query?.Invoke(runId, crawlType));
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

    internal static GccCompetitorAnalysisResolver Build(
        IGccProjectReader projects, IGccCrawlPageReader pages, IGeekCrawlerRagClient rag) =>
        new(projects,
            new GccProjectSiteStructureReader(pages),
            rag,
            NullLogger<GccCompetitorAnalysisResolver>.Instance);

    /// <summary>A crawled page carrying typed blocks, as the crawler stores them. No Html.</summary>
    internal static GeekCrawlerPageDto BlockPage(string url, params object[] blocks) => new(
        Id: Guid.NewGuid(),
        RunId: Guid.NewGuid(),
        Origin: url,
        Url: url,
        FinalUrl: url,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: null,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow,
        Title: "A page",
        Excerpt: null,
        ContentHtml: null,
        Blocks: System.Text.Json.JsonSerializer.SerializeToElement(blocks));

    internal static object Heading(int level, string text) =>
        new Dictionary<string, object> { ["kind"] = "heading", ["level"] = level, ["text"] = text };

    internal static object Paragraph(string text) =>
        new Dictionary<string, object> { ["kind"] = "paragraph", ["text"] = text };

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
    public async Task APageWithNoHtmlStillContributesItsHeadings()
    {
        // This asserted the opposite -- no Html meant the page was skipped. Html is not a validated ingest
        // field, so that dropped pages whose structure was perfectly good. The structure comes from the
        // crawl's typed blocks, which this page has.
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([BlockPage("https://c.test/a", Heading(2, "Pricing"), Paragraph("Text."))]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        var page = Assert.Single(result);
        Assert.Equal("Pricing", Assert.Single(page.Headings).HeadingText);
    }

    [Fact]
    public async Task ExtractsTheHeadingTreeFromTheCrawlsBlocks()
    {
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([BlockPage(
            "https://c.test/pricing",
            Heading(1, "Pricing"),
            Paragraph("Overview text."),
            Heading(2, "Enterprise Plan"),
            Paragraph("Details for enterprise."))]);
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
    public async Task Pages_beyond_the_declared_url_are_read_not_just_the_homepage()
    {
        // The defect this closes. It asked by-seeds for the declared competitor URLs, matched by exact
        // equality, so at most one page per competitor -- its homepage -- could come back. Five
        // competitors carrying 1,777 crawled pages reached the prompt and the provenance guard as five
        // homepage outlines, which is why the blog's competitor: tags resolved to nothing.
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://c.test", "c.test", true, Guid.NewGuid().ToString())]);
        var pages = new FakePages([
            BlockPage("https://c.test/", Heading(1, "Home")),
            BlockPage("https://c.test/blog/ap", Heading(2, "Overcoming Challenges in AP Automation")),
            BlockPage("https://c.test/guides/entry", Heading(2, "How to Start Your AI Journey")),
        ]);
        var resolver = Build(new FakeProjects(Project("https://c.test")), pages, rag);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Equal(3, result.Count);
        var headings = result.SelectMany(p => p.Headings).Select(h => h.HeadingText).ToList();
        Assert.Contains("Overcoming Challenges in AP Automation", headings);
        Assert.Contains("How to Start Your AI Journey", headings);
    }


}
