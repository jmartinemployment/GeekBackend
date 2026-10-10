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

    /// <summary>
    /// One paragraph block per requested page. A tool must be able to quote a partner before it may be
    /// written, so a test that reads the outcome, and not only what was asked, needs the partner's
    /// pages to read back; with <see cref="NoPages"/> every tool outcome here is that refusal.
    /// </summary>
    private sealed class OneBlockPages : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([.. seedUrls.Select(url => Crawled(runId, url))]);

        private static GeekCrawlerPageDto Crawled(Guid runId, string url) => new(
            Id: Guid.NewGuid(),
            RunId: runId,
            Origin: url,
            Url: url,
            FinalUrl: url,
            StatusCode: 200,
            RobotsAllowed: true,
            Html: null,
            FailureReason: null,
            CrawledAtUtc: DateTimeOffset.UtcNow,
            Title: "Page",
            Excerpt: null,
            ContentHtml: null,
            Blocks: JsonSerializer.SerializeToElement(new[]
            {
                new Dictionary<string, string>
                {
                    ["kind"] = "paragraph",
                    ["text"] = "Global payments reconcile against the ledger each night.",
                },
            }));

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    private static GccGroundingResolver Build(
        GccProjectDto project, IGeekCrawlerRagClient rag, IGccCrawlPageReader? pages = null) =>
        new(new FakeProjects(project), rag, new GccTypedPassageReader(pages ?? new NoPages()),
            new GccPublisherPositionsReader(pages ?? new NoPages(), NullLogger<GccPublisherPositionsReader>.Instance),
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

    // The tool page's FAQ (2026-10-10). Until then the operator's questions for a tool were read once,
    // by the page, after every search was over: they were answered from whatever the searches above
    // had brought back, and a question none of those was about was reported as one "no page of the
    // partner's answers" when nothing had looked.

    private const string QuickBooks = "Does Tipalti sync with QuickBooks Online?";
    private const string RiskSegments = "How do customer risk segments influence the cash forecast?";

    private const string BriefWithFaq = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "coreProblem": "The uncontrolled handoff between an approved invoice and the moment cash leaves the bank.",
            "perTool": {
              "tipalti.com": {
                "faqQuestions": "Does Tipalti sync with QuickBooks Online?\n\nHow do customer risk segments influence the cash forecast?\nDoes Tipalti sync with QuickBooks Online?"
              },
              "bill.com": { "faqQuestions": "Does Bill pay international vendors?" }
            }
          }
        }
        """;

    /// <summary>Every question lands on the same page, each finding its own passage and one they all share.</summary>
    private sealed class OnePageRag : IGeekCrawlerRagClient
    {
        private readonly RecordingRag inner = new();
        private int asked;

        public bool IsEnabled => true;

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(GeekCrawlerRagQuery query, CancellationToken ct = default)
        {
            asked++;
            return Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
            {
                RunId = query.RunId,
                Pages =
                [
                    new GccQuoteablePage(
                        "https://tipalti.com/product-updates/", "Product updates", [],
                        ["On every answer.", "Found by question " + asked + "."],
                        Scores: [new GccPassageScore(0.5), new GccPassageScore(asked)]),
                ],
            });
        }

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            inner.EnqueueIndexAsync(runId, ct);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            inner.GetIndexStatusAsync(runId, ct);

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            inner.HostsIndexedAsync(urls, crawlType, ct);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            inner.QueryAsync(need, runId, crawlType, host, topK, preferParent, preferChild, entityNames, retrievalMode, anchorToolLookup, ct);

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            inner.IndexTemplatesAsync(templates, ct);

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            inner.QueryTemplatesAsync(need, topK, channel, entityTags, ct);

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            inner.GetPageTextAsync(pageId, ct, runId);

        public Task<JsonElement?> RunDiagnosticAsync(
            string endpoint, object? payload = null, CancellationToken ct = default) =>
            inner.RunDiagnosticAsync(endpoint, payload, ct);
    }

    /// <summary>
    /// The brief asks this partner three questions. Until 2026-10-10 a page the first had returned was
    /// dropped whole from the second's and the third's answers, with the passages they had found on it.
    /// </summary>
    [Fact]
    public async Task A_later_question_adds_its_passages_to_a_page_an_earlier_question_returned()
    {
        var project = Project("https://tipalti.com/");

        var outcome = await Build(project, new OnePageRag(), new OneBlockPages())
            .ResolveAsync(Create(project.Id, Brief), "tool");

        var page = Assert.Single(outcome.Pages, p => p.Url == "https://tipalti.com/product-updates/");
        Assert.Equal(
            ["On every answer.", "Found by question 1.", "Found by question 2.", "Found by question 3."],
            page.Paragraphs);

        // The scores stay in step with the passages they score.
        Assert.Equal([0.5, 1, 2, 3], page.Scores!.Select(s => s.Score));
    }

    [Fact]
    public void A_page_with_nothing_new_on_it_is_left_as_it_was()
    {
        var held = new GccQuoteablePage("https://tipalti.com/a/", "A", [], ["One.", "Two."]);
        var later = new GccQuoteablePage("https://tipalti.com/a/", "A", [], ["Two.", "One."]);

        Assert.Same(held, GccGroundingResolver.WithLaterPassages(held, later));
    }

    [Fact]
    public void Passages_are_added_without_scores_when_either_answer_carries_none()
    {
        var held = new GccQuoteablePage("https://tipalti.com/a/", "A", [], ["One."], Scores: [new GccPassageScore(0.4)]);
        var later = new GccQuoteablePage("https://tipalti.com/a/", "A", [], ["Two."]);

        var merged = GccGroundingResolver.WithLaterPassages(held, later);

        Assert.Equal(["One.", "Two."], merged.Paragraphs);
        Assert.Null(merged.Scores);
    }

    private static IReadOnlyList<GeekCrawlerRagQuery> FaqQueries(RecordingRag rag) =>
        [.. PartnerQueries(rag).Where(q => q.TopK == GccGroundingResolver.FaqTopK)];

    [Fact]
    public async Task Every_faq_question_the_tool_page_will_be_asked_is_searched_for_in_its_partners_crawl()
    {
        var project = Project("https://tipalti.com/");
        var rag = new RecordingRag();
        var create = Create(project.Id, BriefWithFaq);

        var outcome = await Build(project, rag, new OneBlockPages()).ResolveAsync(create, "tool");

        // The page and the search read one list, so the assertion is on the page's own reader.
        var asked = GccNicheFramingReader.ToolFaqQuestions(
            BriefWithFaq, project.PartnerUrls,
            GccRequiredToolMentions.AnchorLookup(BriefWithFaq, project.PartnerUrls).Values.Single());
        Assert.Equal([QuickBooks, RiskSegments], asked);

        var searches = FaqQueries(rag);
        Assert.Equal(asked, searches.Select(q => q.Need));
        Assert.All(searches, q =>
        {
            Assert.Equal(TipaltiRun, q.RunId);
            Assert.Null(q.Keyword);
        });

        // Filed by host and question, one entry a search, and no other partner's questions among them.
        Assert.NotNull(outcome.FaqEvidence);
        Assert.Equal(asked, outcome.FaqEvidence.Select(e => e.Question));
        Assert.All(outcome.FaqEvidence, e => Assert.Equal("tipalti.com", e.Host));
        Assert.DoesNotContain(rag.Asked, q => q.Need.Contains("international vendors", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_a_faq_search_finds_stays_out_of_the_passages_every_writer_reads()
    {
        var project = Project("https://tipalti.com/");

        var without = await Build(project, new RecordingRag(), new OneBlockPages()).ResolveAsync(Create(project.Id, Brief), "tool");
        var withFaq = new RecordingRag();
        var brief = Brief.Replace(
            "\"tipalti.com\": {",
            "\"tipalti.com\": { \"faqQuestions\": \"Does Tipalti sync with QuickBooks Online?\",",
            StringComparison.Ordinal);
        var outcome = await Build(project, withFaq, new OneBlockPages()).ResolveAsync(Create(project.Id, brief), "tool");

        Assert.Single(FaqQueries(withFaq));
        Assert.Equal(without.Pages.Select(p => p.Url), outcome.Pages.Select(p => p.Url));
        var entry = Assert.Single(outcome.FaqEvidence!);
        Assert.DoesNotContain(outcome.Pages, p => entry.Pages.Any(f => f.Url == p.Url));
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public async Task A_run_that_writes_no_tool_page_searches_for_no_faq_question(string contentType)
    {
        var project = Project("https://tipalti.com/");
        var rag = new RecordingRag();

        var outcome = await Build(project, rag).ResolveAsync(Create(project.Id, BriefWithFaq), contentType);

        Assert.DoesNotContain(rag.Asked, q => q.Need == QuickBooks || q.Need == RiskSegments);
        // Null, not empty: nothing was searched for, which is not the same as finding nothing.
        Assert.Null(outcome.FaqEvidence);
    }

    [Fact]
    public async Task A_tool_run_whose_brief_has_no_faq_questions_searched_and_has_none()
    {
        var project = Project("https://tipalti.com/");

        var outcome = await Build(project, new RecordingRag(), new OneBlockPages()).ResolveAsync(Create(project.Id, Brief), "tool");

        Assert.NotNull(outcome.FaqEvidence);
        Assert.Empty(outcome.FaqEvidence);
    }

    /// <summary>Answers every search but the FAQ questions it is told to find nothing for, or to fail on.</summary>
    private sealed class FaqRag(string? findsNothingFor = null, string? failsOn = null, string? retrieval = null) : IGeekCrawlerRagClient
    {
        private readonly RecordingRag inner = new();

        public bool IsEnabled => true;

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(GeekCrawlerRagQuery query, CancellationToken ct = default)
        {
            if (query.Need == failsOn)
            {
                return Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
                {
                    RunId = query.RunId, Pages = [], Failed = true, Error = "The index timed out.",
                });
            }

            if (query.Need == findsNothingFor)
            {
                return Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
                {
                    RunId = query.RunId, Pages = [], Warning = "No chunks for runId=" + query.RunId,
                });
            }

            return retrieval is null ? inner.QueryAsync(query, ct) : SaysHowItOrdered(query, ct);
        }

        /// <summary>The same answer, with the library's word for how it ordered the passages.</summary>
        private async Task<GeekCrawlerRagQueryResult?> SaysHowItOrdered(GeekCrawlerRagQuery query, CancellationToken ct)
        {
            var answered = await inner.QueryAsync(query, ct);
            return answered is null
                ? null
                : new GeekCrawlerRagQueryResult
                {
                    RunId = answered.RunId, Pages = answered.Pages, Warning = answered.Warning, Retrieval = retrieval,
                };
        }

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            inner.EnqueueIndexAsync(runId, ct);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            inner.GetIndexStatusAsync(runId, ct);

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            inner.HostsIndexedAsync(urls, crawlType, ct);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            inner.QueryAsync(need, runId, crawlType, host, topK, preferParent, preferChild, entityNames, retrievalMode, anchorToolLookup, ct);

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            inner.IndexTemplatesAsync(templates, ct);

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            inner.QueryTemplatesAsync(need, topK, channel, entityTags, ct);

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            inner.GetPageTextAsync(pageId, ct, runId);

        public Task<JsonElement?> RunDiagnosticAsync(
            string endpoint, object? payload = null, CancellationToken ct = default) =>
            inner.RunDiagnosticAsync(endpoint, payload, ct);
    }

    [Fact]
    public async Task A_faq_search_is_filed_with_the_librarys_word_for_how_it_ordered_the_passages()
    {
        var project = Project("https://tipalti.com/");
        var brief = Brief.Replace(
            "\"tipalti.com\": {",
            "\"tipalti.com\": { \"faqQuestions\": \"Does Tipalti sync with QuickBooks Online?\",",
            StringComparison.Ordinal);

        var ranked = await Build(project, new FaqRag(retrieval: "llamaindex-hybrid+rerank"), new OneBlockPages())
            .ResolveAsync(Create(project.Id, brief), "tool");
        var unranked = await Build(project, new FaqRag(retrieval: "llamaindex-hybrid"), new OneBlockPages())
            .ResolveAsync(Create(project.Id, brief), "tool");

        Assert.True(Assert.Single(ranked.FaqEvidence!).Reranked);
        var entry = Assert.Single(unranked.FaqEvidence!);
        Assert.Equal("llamaindex-hybrid", entry.Retrieval);
        Assert.False(entry.Reranked);
    }

    [Fact]
    public async Task A_faq_search_that_finds_nothing_is_an_entry_with_no_passages_and_refuses_nothing()
    {
        var project = Project("https://tipalti.com/");

        var outcome = await Build(project, new FaqRag(findsNothingFor: RiskSegments), new OneBlockPages())
            .ResolveAsync(Create(project.Id, BriefWithFaq), "tool");

        Assert.False(outcome.Refused);
        var entries = outcome.FaqEvidence!;
        Assert.NotEmpty(Assert.Single(entries, e => e.Question == QuickBooks).Pages);
        Assert.Empty(Assert.Single(entries, e => e.Question == RiskSegments).Pages);
        // The partner returned evidence for the page's other searches, so it is not one without passages,
        // and the index's own "no chunks" line for the question is not raised as a warning about the partner.
        Assert.Empty(outcome.PartnersWithoutPassages ?? []);
        Assert.DoesNotContain(outcome.Warnings, w => w.Contains("No chunks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_faq_search_that_fails_refuses_the_run_as_any_failed_search_does()
    {
        var project = Project("https://tipalti.com/");

        var outcome = await Build(project, new FaqRag(failsOn: RiskSegments), new OneBlockPages())
            .ResolveAsync(Create(project.Id, BriefWithFaq), "tool");

        Assert.True(outcome.Refused);
        Assert.Equal("The index timed out.", outcome.Refusal);
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
