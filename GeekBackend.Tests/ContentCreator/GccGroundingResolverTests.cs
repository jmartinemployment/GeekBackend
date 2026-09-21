using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The refusal paths. Each asserts that absent evidence stops the draft and says why — the whole
/// point of the gate, since the defect it replaces was absent evidence dropping out silently while
/// generation continued.
/// </summary>
public class GccGroundingResolverTests
{
    private static GccCreateDto Create(Guid? projectId) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar",
        Topic: "AI implementation for SMBs",
        Notes: null,
        ProjectSiteRunId: null,
        SiteSectionJson: null,
        BriefJson: null,
        ResearchJson: null,
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow,
        Department: "marketing",
        ProjectId: projectId);

    private static GccProjectDto Project(params string[] partnerUrls) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        Name: "Acme",
        Code: null,
        Description: null,
        Status: "active",
        SiteUrl: "https://acme.test",
        ProjectSiteRunId: null,
        Department: "marketing",
        PartnerUrls: partnerUrls,
        CompetitorUrls: [],
        StartDate: new DateOnly(2026, 1, 1),
        DueDate: null,
        FinishedDate: null,
        EstimatedHours: null,
        Budget: null,
        BudgetCurrency: null,
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);

    private sealed class FakeProjects(GccProjectDto? project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(project);
    }

    private sealed class FakeRag(
        IReadOnlyList<GeekCrawlerRagHostIndex>? hosts = null,
        GeekCrawlerRagQueryResult? result = null) : IGeekCrawlerRagClient
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
            Task.FromResult(result);

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

    private static GccGroundingResolver Build(IGccProjectReader projects, IGeekCrawlerRagClient rag) =>
        new(projects, rag, NullLogger<GccGroundingResolver>.Instance);

    [Fact]
    public async Task ATypeThatDeclaresNoEvidenceIsNeverRefused()
    {
        var resolver = Build(new FakeProjects(null), new FakeRag());

        var outcome = await resolver.ResolveAsync(Create(null), "linkedin");

        Assert.False(outcome.Refused);
    }

    [Fact]
    public async Task ACreateWithNoProjectIsRefused()
    {
        var resolver = Build(new FakeProjects(null), new FakeRag());

        var outcome = await resolver.ResolveAsync(Create(null), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("no project", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProjectWithNoPartnerUrlsIsRefused()
    {
        var resolver = Build(new FakeProjects(Project()), new FakeRag());

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("no partner URLs", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartnerUrlsWithNoIndexedCrawlAreRefused()
    {
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", false, null)]);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("indexed crawl", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFailedQueryIsRefusedAndNeverReadAsSuccess()
    {
        // The regression this guards: Failed with an empty page list looks exactly like "no
        // results" unless Failed is checked first.
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult
            {
                RunId = Guid.NewGuid(),
                Pages = [],
                Failed = true,
                Error = "index unreachable",
            });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Equal("index unreachable", outcome.Refusal);
    }

    [Fact]
    public async Task ANullClientResultIsRefused()
    {
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: null);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
    }

    [Fact]
    public async Task ASuccessfulQueryThatFoundNothingIsStillRefused()
    {
        // Not an error, but nothing citable was retrieved, so the draft cannot be grounded.
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [], Failed = false });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("citable passage", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrievedPagesAreReturnedWhenEvidenceExists()
    {
        var page = new GccQuoteablePage("https://p.test/a", "A", [], ["body"]);
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [page], Failed = false });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.False(outcome.Refused);
        Assert.Single(outcome.Pages);
        Assert.Equal("https://p.test/a", outcome.Pages[0].Url);
    }
}
