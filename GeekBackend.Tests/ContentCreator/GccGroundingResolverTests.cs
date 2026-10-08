using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
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
    private static readonly Guid SiteRun = Guid.NewGuid();

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
        // Every project has one: ProjectForm refuses to create one without an indexed site crawl,
        // and generation refuses without it too -- the publisher's own pages are what stops a
        // piece repeating what the site already says.
        ProjectSiteRunId: SiteRun,
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

    /// <summary>The partner run a refusal has to name, fixed so the assertion can look for it.</summary>
    private static readonly Guid PartnerRun = Guid.NewGuid();

    private sealed class FakeProjects(GccProjectDto? project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(project);
    }

    /// <summary>
    /// Answers for one crawl type and returns nothing for the others.
    ///
    /// <para>
    /// It used to ignore <c>crawlType</c> and return the same page for all three, which cannot
    /// happen: the project site, the partners and the competitors are three separately declared URL
    /// sets, and the site run returns the publisher's own pages. That degenerate shape mattered once
    /// dedupe became per crawl type (2026-10-01) — the one page arrived as own-site AND partner
    /// evidence, and a test asserting a single passage failed on a state production cannot reach.
    /// </para>
    /// </summary>
    private sealed class FakeRag(
        IReadOnlyList<GeekCrawlerRagHostIndex>? hosts = null,
        GeekCrawlerRagQueryResult? result = null,
        string? answersFor = CrawlTypes.Partner) : IGeekCrawlerRagClient
    {
        public bool IsEnabled => true;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            Task.FromResult(hosts ?? []);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            Task.FromResult(
                answersFor is null || string.Equals(crawlType, answersFor, StringComparison.OrdinalIgnoreCase)
                    ? result
                    : result is null
                        ? null
                        : new GeekCrawlerRagQueryResult { RunId = result.RunId, Pages = [] });

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

    private sealed class FakePages(IReadOnlyList<GeekCrawlerPageDto>? pages = null) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult(pages ?? []);

        // These fakes exist to exercise seed lookup. Whole-run paging belongs to the tool resolver,
        // which has its own fake, so answering pages here would assert something this file does not.
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    private static GccGroundingResolver Build(
        IGccProjectReader projects,
        IGeekCrawlerRagClient rag,
        IGccCrawlPageReader? pages = null) =>
        new(projects, rag, new GccTypedPassageReader(pages ?? new FakePages()),
            new GccPublisherPositionsReader(pages ?? new FakePages(), NullLogger<GccPublisherPositionsReader>.Instance),
            NullLogger<GccGroundingResolver>.Instance);

    private static GeekCrawlerPageDto CrawledPage(string url, string blocksJson) => new(
        Id: Guid.NewGuid(),
        RunId: Guid.NewGuid(),
        Origin: "https://p.test",
        Url: url,
        FinalUrl: url,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: null,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow,
        Title: "A",
        Excerpt: null,
        ContentHtml: null,
        Blocks: JsonDocument.Parse(blocksJson).RootElement);

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

        var outcome = await resolver.ResolveAsync(Create(null), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains("no project", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProjectWithNoPartnerUrlsIsRefusedForATool()
    {
        // Still refused, and now from one place instead of two. The per-URL loop asks one binary
        // question -- does an indexed crawl exist for this declared URL? -- and a project that
        // declares no partners asks it zero times. What refuses is the citation rule at the end:
        // a tool must cite partner evidence and none was retrieved.
        //
        // It used to be refused twice, by two rules that could disagree about when.
        var resolver = Build(
            new FakeProjects(Project()),
            new FakeRag(result: new GeekCrawlerRagQueryResult { RunId = SiteRun, Pages = [] }));

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains("must cite partner", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartnerUrlsWithNoIndexedCrawlAreRefused()
    {
        var rag = new FakeRag(hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", false, null)]);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

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

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

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

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
    }

    [Fact]
    public async Task ASuccessfulQueryThatFoundNothingIsStillRefused()
    {
        // Not an error, but nothing citable was retrieved at all, so a tool page has no partner to quote.
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [], Failed = false });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains("citable passage", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Answers per partner run: a run in <paramref name="emptyRuns"/> returns no pages, every other
    /// partner run returns one page of its own. Competitor and own-site runs return nothing.
    /// </summary>
    private sealed class PerRunRag(
        IReadOnlyList<GeekCrawlerRagHostIndex> hosts,
        IReadOnlySet<Guid>? emptyRuns = null,
        string? warning = null) : IGeekCrawlerRagClient
    {
        private readonly FakeRag _rest = new(hosts);

        public bool IsEnabled => true;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            _rest.EnqueueIndexAsync(runId, ct);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            _rest.GetIndexStatusAsync(runId, ct);

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            Task.FromResult(hosts);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default)
        {
            var isPartner = string.Equals(crawlType, CrawlTypes.Partner, StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<GccQuoteablePage> pages = isPartner && !(emptyRuns?.Contains(runId) ?? false)
                ? [new GccQuoteablePage($"https://run-{runId}.test/a", "A", [], ["body"])]
                : [];
            return Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
            {
                RunId = runId,
                Pages = [.. pages],
                Warning = isPartner ? warning : null,
            });
        }

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            _rest.IndexTemplatesAsync(templates, ct);

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            _rest.QueryTemplatesAsync(need, topK, channel, entityTags, ct);

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            _rest.GetPageTextAsync(pageId, ct, runId);

        public Task<JsonElement?> RunDiagnosticAsync(
            string endpoint, object? payload = null, CancellationToken ct = default) =>
            _rest.RunDiagnosticAsync(endpoint, payload, ct);

        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            _rest.GetCapabilitiesAsync(ct);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public async Task One_unindexed_partner_of_two_refuses_a_pillar_or_blog_naming_it(string type)
    {
        // The resolver refused only when EVERY declared URL was unindexed, so one dropped partner crawl
        // out of five was skipped silently and the draft was asked to name a partner it had nothing on.
        var rag = new PerRunRag(
        [
            new GeekCrawlerRagHostIndex("https://melio.test", "melio.test", true, Guid.NewGuid().ToString()),
            new GeekCrawlerRagHostIndex("https://ramp.test", "ramp.test", false, null),
        ]);
        var resolver = Build(new FakeProjects(Project("https://melio.test", "https://ramp.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), type);

        Assert.True(outcome.Refused);
        Assert.Contains("https://ramp.test", outcome.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain("https://melio.test", outcome.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_url_the_index_never_answered_for_counts_as_unindexed()
    {
        var rag = new PerRunRag(
            [new GeekCrawlerRagHostIndex("https://melio.test", "melio.test", true, Guid.NewGuid().ToString())]);
        var resolver = Build(new FakeProjects(Project("https://melio.test", "https://ramp.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("https://ramp.test", outcome.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_index_is_named_as_an_outage_not_a_dropped_crawl()
    {
        var rag = new PerRunRag([]);
        var resolver = Build(new FakeProjects(Project("https://melio.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("could not be reached", outcome.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain("Re-crawl", outcome.Refusal!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task A_partner_that_returns_no_passage_is_reported_by_name_not_refused_here(string type)
    {
        // What it refuses depends on the type -- the pillar or blog, or only that partner's tool page --
        // so the resolver names it and GccGenerationCoordinator.PartnerEvidenceRefusal decides.
        var melioRun = Guid.NewGuid();
        var rampRun = Guid.NewGuid();
        var rag = new PerRunRag(
        [
            new GeekCrawlerRagHostIndex("https://melio.test", "melio.test", true, melioRun.ToString()),
            new GeekCrawlerRagHostIndex("https://ramp.test", "ramp.test", true, rampRun.ToString()),
        ], emptyRuns: new HashSet<Guid> { rampRun });
        // The melio page's crawled blocks: a tool page quotes from typed blocks, and refuses when the
        // retrieved pages cannot be read back as blocks -- a different refusal from the one tested here.
        var crawled = new FakePages([CrawledPage(
            $"https://run-{melioRun}.test/a", """[{"kind":"paragraph","text":"Melio pays bills from your bank."}]""")]);
        var resolver = Build(new FakeProjects(Project("https://melio.test", "https://ramp.test")), rag, crawled);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), type);

        Assert.False(outcome.Refused);
        var partner = Assert.Single(outcome.PartnersWithoutPassages!);
        Assert.Contains("ramp.test", partner, StringComparison.Ordinal);
        Assert.Single(outcome.Pages);
    }

    [Fact]
    public async Task Every_declared_partner_answering_is_not_refused_and_keeps_its_warnings()
    {
        var rag = new PerRunRag(
        [
            new GeekCrawlerRagHostIndex("https://melio.test", "melio.test", true, Guid.NewGuid().ToString()),
            new GeekCrawlerRagHostIndex("https://ramp.test", "ramp.test", true, Guid.NewGuid().ToString()),
        ], warning: "reranker unavailable; vector order used");
        var resolver = Build(new FakeProjects(Project("https://melio.test", "https://ramp.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.False(outcome.Refused);
        Assert.Equal(2, outcome.Pages.Count);
        // Two from the library, one per partner run; and one because this fake site has no page in its
        // crawl run, so the writer had none of the publisher's own positions -- said, not silent.
        Assert.Equal(2, outcome.Warnings.Count(w => w.Contains("reranker unavailable", StringComparison.Ordinal)));
        var positions = Assert.Single(outcome.Warnings, w => w.Contains("publisher's own positions", StringComparison.Ordinal));
        Assert.Contains("https://acme.test", positions, StringComparison.Ordinal);
        Assert.Empty(outcome.PublisherPositions ?? []);
    }

    [Fact]
    public async Task RetrievedBlocksArriveAsTypedParagraphsCarryingTheirSource()
    {
        var page = new GccQuoteablePage("https://p.test/a", "A", [], ["body"]);
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [page], Failed = false });
        var crawled = new FakePages([CrawledPage(
            "https://p.test/a",
            """[{"kind":"quote","text":"Latency fell by half."},{"kind":"code","text":"SELECT 1;"}]""")]);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag, crawled);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        var passage = Assert.Single(outcome.PartnerPassages);
        var quote = Assert.IsType<QuoteParagraph>(passage.Content[0]);
        Assert.Equal("https://p.test/a", quote.Cite);
        Assert.IsType<CodeParagraph>(passage.Content[1]);
    }

    [Fact]
    public async Task MissingBlocksDoNotTurnATypeThatNeverQuotesIntoARefusal()
    {
        // For a type that does not quote, typed shape is an enrichment of evidence already proven
        // present, and a refusal is for missing evidence rather than missing shape. A pillar is
        // written from the retrieved prose either way.
        var page = new GccQuoteablePage("https://p.test/a", "A", [], ["body"]);
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [page], Failed = false });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag, new FakePages());

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "pillar");

        Assert.False(outcome.Refused);
        Assert.Single(outcome.Pages);
        Assert.Empty(outcome.PartnerPassages);
    }

    [Fact]
    public async Task MissingBlocksRefuseATypeThatMustQuoteAPartner()
    {
        // The other half of the same rule, and the reason it is split by content type. A tool page's
        // block quotation is cut from the typed blocks, so for a tool they are the evidence and not
        // a nicer shape for it: pages retrieved but unreadable means the quote has no source.
        //
        // It refuses here rather than three model calls later, where it arrived as "the tool page
        // does not carry a verifiable block quotation" -- the writer blamed for the crawl store
        // being unreadable, and an operator sent to the prompt to fix it.
        var page = new GccQuoteablePage("https://p.test/a", "A", [], ["body"]);
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [page], Failed = false });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag, new FakePages());

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains("typed blocks", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
        // Named for what is actually wrong -- the pages are indexed, their blocks are not readable.
        Assert.Contains("re-crawl", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_library_failure_names_the_crawl_type_and_the_url_that_failed()
    {
        // The one risk hoisting the resolve out of the per-type fan-out introduced. A failure used to
        // belong to one content type; now it fails the whole generate, so without the URL and the crawl
        // type in the message an operator sees three dead drafts and no cause to act on. Named by the
        // URL the operator entered -- a run id means nothing to them (Jeff, 2026-10-05).
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, PartnerRun.ToString())],
            result: new GeekCrawlerRagQueryResult
            {
                RunId = PartnerRun,
                Pages = [],
                Failed = true,
                Error = null,
                Warning = null,
            });
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains(CrawlTypes.Partner, outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://p.test", outcome.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain(PartnerRun.ToString(), outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_library_answer_names_the_crawl_type_and_the_url_too()
    {
        // Null from the client is the library not answering at all, which reads differently from a
        // failed result -- and has to carry the same two facts.
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, PartnerRun.ToString())],
            result: null);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains(CrawlTypes.Partner, outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://p.test", outcome.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain(PartnerRun.ToString(), outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrievedPagesAreReturnedWhenEvidenceExists()
    {
        var page = new GccQuoteablePage("https://p.test/a", "A", [], ["body"]);
        var rag = new FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://p.test", "p.test", true, Guid.NewGuid().ToString())],
            result: new GeekCrawlerRagQueryResult { RunId = Guid.NewGuid(), Pages = [page], Failed = false });
        var crawled = new FakePages([CrawledPage(
            "https://p.test/a", """[{"kind":"paragraph","text":"Approvals fell from nine days to two."}]""")]);
        var resolver = Build(new FakeProjects(Project("https://p.test")), rag, crawled);

        var outcome = await resolver.ResolveAsync(Create(Guid.NewGuid()), "tool");

        Assert.False(outcome.Refused);
        Assert.Single(outcome.Pages);
        Assert.Equal("https://p.test/a", outcome.Pages[0].Url);
    }
}
