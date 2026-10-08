using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A partner run is asked from the brief (Geek-Crawler-Rag plans/retrieval-from-the-brief.md P5):
/// the tool's core problem, then one question per evidence row with the vendor's solution for the
/// index's meaning half and the row's terms for its keyword half. Without a brief the keyword alone
/// is asked at the old depth. These tests record the questions the resolver sends; what the index
/// answers is the index's business.
/// </summary>
public class GccGroundingEvidenceQuestionsTests
{
    private static readonly Guid SiteRun = Guid.NewGuid();
    private static readonly Guid TipaltiRun = Guid.NewGuid();

    private const string Brief = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "coreProblem": "The uncontrolled handoff between an approved invoice and the moment cash leaves the bank.",
            "evidence": [
              { "problem": "Approval is informal.", "solution": "Approval workflows route each bill with an audit trail.", "terms": ["approval workflow", "audit trail"] }
            ],
            "perTool": {
              "tipalti.com": {
                "coreProblem": "International vendors, multiple entities and contractors need controlled global payment execution.",
                "evidence": [
                  { "problem": "Cannot reconcile global payments.", "solution": "Automated payment reconciliation syncs payment results with the ERP and sub-ledgers.", "terms": ["payment reconciliation", "multi-entity"] }
                ]
              }
            }
          }
        }
        """;

    private static GccCreateDto Create(Guid projectId, string? briefJson) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "tool",
        Topic: "Accounts Payable: Automated Payment Execution",
        Notes: null,
        ProjectSiteRunId: null,
        SiteSectionJson: null,
        BriefJson: briefJson,
        ResearchJson: null,
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow,
        Department: "accounting",
        ProjectId: projectId);

    private static GccProjectDto Project(params string[] partnerUrls) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        Name: "Accounts Payable: Automated Payment Execution",
        Code: null,
        Description: null,
        Status: "active",
        SiteUrl: "https://geekatyourspot.com/",
        ProjectSiteRunId: SiteRun,
        Department: "accounting",
        PartnerUrls: partnerUrls,
        CompetitorUrls: [],
        StartDate: new DateOnly(2026, 10, 1),
        DueDate: null,
        FinishedDate: null,
        EstimatedHours: null,
        Budget: null,
        BudgetCurrency: null,
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);

    private sealed class FakeProjects(GccProjectDto project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<GccProjectDto?>(project);
    }

    /// <summary>Records every query as the resolver asks it, through the record overload the HTTP client sends.</summary>
    private sealed class RecordingRag : IGeekCrawlerRagClient
    {
        public List<GeekCrawlerRagQuery> Asked { get; } = [];

        public bool IsEnabled => true;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerRagHostIndex>>(
                [.. urls.Select(u => new GeekCrawlerRagHostIndex(u, "tipalti.com", true, TipaltiRun.ToString(), crawlType))]);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(GeekCrawlerRagQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);
            return Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
            {
                RunId = query.RunId,
                Pages = [new GccQuoteablePage("https://tipalti.com/" + Asked.Count + "/", "Page " + Asked.Count, [], ["Some text."])],
            });
        }

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("the resolver asks through the record overload, which carries the keyword");

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

        public Task<JsonElement?> RunDiagnosticAsync(
            string endpoint, object? payload = null, CancellationToken ct = default) =>
            Task.FromResult<JsonElement?>(null);

        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            Task.FromResult(new GeekCrawlerRagCapabilities());
    }

    private sealed class NoPages : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    private static GccGroundingResolver Build(GccProjectDto project, IGeekCrawlerRagClient rag) =>
        new(new FakeProjects(project), rag, new GccTypedPassageReader(new NoPages()),
            new GccPublisherPositionsReader(new NoPages(), NullLogger<GccPublisherPositionsReader>.Instance),
            NullLogger<GccGroundingResolver>.Instance);

    private static IReadOnlyList<GeekCrawlerRagQuery> PartnerQueries(RecordingRag rag) =>
        [.. rag.Asked.Where(q => string.Equals(q.CrawlType, CrawlTypes.Partner, StringComparison.OrdinalIgnoreCase))];

    [Fact]
    public async Task A_partner_run_is_asked_its_core_problem_then_one_question_per_evidence_row()
    {
        var project = Project("https://tipalti.com/");
        var rag = new RecordingRag();

        await Build(project, rag).ResolveAsync(Create(project.Id, Brief), "tool");

        var asked = PartnerQueries(rag);
        Assert.Equal(3, asked.Count);

        // The tool's own core problem, no keyword, eight passages.
        Assert.StartsWith("International vendors", asked[0].Need, StringComparison.Ordinal);
        Assert.Null(asked[0].Keyword);
        Assert.Equal(8, asked[0].TopK);

        // The category's row comes first (rows add), then Tipalti's own: the vendor's solution as
        // the need and the row's terms, joined, as the keyword.
        Assert.StartsWith("Approval workflows route", asked[1].Need, StringComparison.Ordinal);
        Assert.Equal("approval workflow audit trail", asked[1].Keyword);
        Assert.StartsWith("Automated payment reconciliation", asked[2].Need, StringComparison.Ordinal);
        Assert.Equal("payment reconciliation multi-entity", asked[2].Keyword);
        Assert.All(asked, q => Assert.Equal(TipaltiRun, q.RunId));
    }

    [Fact]
    public async Task Without_a_brief_the_partner_run_is_asked_the_keyword_alone_at_the_old_depth()
    {
        var project = Project("https://tipalti.com/");
        var rag = new RecordingRag();

        await Build(project, rag).ResolveAsync(Create(project.Id, null), "tool");

        var only = Assert.Single(PartnerQueries(rag));
        Assert.Equal("Automated Payment Execution", only.Need);
        Assert.Null(only.Keyword);
        Assert.Equal(32, only.TopK);
    }

    [Fact]
    public async Task The_project_site_is_still_asked_its_one_question()
    {
        var project = Project("https://tipalti.com/");
        var rag = new RecordingRag();

        await Build(project, rag).ResolveAsync(Create(project.Id, Brief), "tool");

        var site = Assert.Single(rag.Asked, q => string.Equals(q.CrawlType, CrawlTypes.ProjectSite, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Automated Payment Execution", site.Need);
        Assert.Null(site.Keyword);
        Assert.Equal(SiteRun, site.RunId);
    }
}
