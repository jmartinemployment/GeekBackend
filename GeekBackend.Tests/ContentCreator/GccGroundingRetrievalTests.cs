using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What each content type goes and fetches, and where it lands.
///
/// <para>
/// One table used to answer two questions — what a type must cite, and what it fetches. Pillar,
/// Blog and TechArticle were removed from it on 2026-09-22 because citing was never their
/// requirement, and the fetch went with them: for a week they were obliged by
/// <c>GccRequiredToolMentions</c> to name every declared partner while being handed nothing about
/// any of them. Competitors were never in the table at all, for any type, so nothing ever retrieved
/// a rival page even though the query string for it was written and the corpus was indexed.
/// </para>
/// </summary>
public class GccGroundingRetrievalTests
{
    private const string PartnerUrl = "https://partner.test/pricing";
    private const string CompetitorUrl = "https://rival.test/services";

    private const string SiteUrl = "https://acme.test/ap-guide";

    private static readonly Guid PartnerRun = Guid.NewGuid();
    private static readonly Guid CompetitorRun = Guid.NewGuid();
    private static readonly Guid SiteRun = Guid.NewGuid();

    private static GccCreateDto Create(Guid? projectId, string type = "pillar") => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: type, Topic: "Accounts Payable: Automated Data Entry", Notes: null,
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: null, ResearchJson: null,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow,
        Department: "marketing", ProjectId: projectId);

    private static GccProjectDto Project(
        IReadOnlyList<string> partners, IReadOnlyList<string> competitors) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), Name: "Acme", Code: null, Description: null,
        Status: "active", SiteUrl: "https://acme.test", ProjectSiteRunId: SiteRun,
        Department: "marketing", PartnerUrls: partners, CompetitorUrls: competitors,
        StartDate: new DateOnly(2026, 1, 1), DueDate: null, FinishedDate: null,
        EstimatedHours: null, Budget: null, BudgetCurrency: null,
        CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private sealed class FakeProjects(GccProjectDto? project) : IGccProjectReader
    {
        public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(project);
    }

    /// <summary>
    /// Answers per crawl type, and records which types were actually asked about. The existing fake
    /// in GccGroundingResolverTests returns one result whatever it is handed, which cannot show that
    /// partner and competitor are queried separately or that their pages stay apart.
    /// </summary>
    private sealed class CrawlTypeRag(bool partnerIndexed = true, bool competitorIndexed = true)
        : IGeekCrawlerRagClient
    {
        public List<string> Queried { get; } = [];

        /// <summary>How deep each crawl type was asked to go. Partner and prose want different depths.</summary>
        public List<(string CrawlType, int TopK)> Depths { get; } = [];

        public bool IsEnabled => true;

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default)
        {
            var rows = urls.Select(u => u.Contains("partner", StringComparison.Ordinal)
                ? new GeekCrawlerRagHostIndex(u, "partner.test", partnerIndexed, PartnerRun.ToString())
                : new GeekCrawlerRagHostIndex(u, "rival.test", competitorIndexed, CompetitorRun.ToString()))
                .ToList();
            return Task.FromResult<IReadOnlyList<GeekCrawlerRagHostIndex>>(rows);
        }

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default)
        {
            Queried.Add(crawlType ?? "(none)");
            Depths.Add((crawlType ?? "", topK));
            var page = crawlType switch
            {
                CrawlTypes.Competitors => new GccQuoteablePage(
                    CompetitorUrl, "Rival services", [new HeadingDto(2, "What we do")],
                    ["We run AP projects end to end."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
                CrawlTypes.ProjectSite => new GccQuoteablePage(
                    SiteUrl, "Our AP guide", [new HeadingDto(2, "How AP automation works")],
                    ["We published this last quarter."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
                _ => new GccQuoteablePage(
                    PartnerUrl, "Partner pricing", [new HeadingDto(2, "Plans")],
                    ["Billed per document."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            };

            return Task.FromResult<GeekCrawlerRagQueryResult?>(
                new GeekCrawlerRagQueryResult { RunId = runId, Pages = [page] });
        }

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
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

    private sealed class NoPages : IGccCrawlPageReader
    {
        // One paragraph block per requested seed. These tests are about which corpus a page lands
        // in, and a tool must be able to quote a partner before it may be written -- a fake that
        // answered nothing would refuse every tool case here for a reason none of them is about.
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>(
                seedUrls.Select(url => Crawled(runId, url)).ToList());

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
                    ["text"] = "Approval time fell from nine days to two across every department.",
                },
            }));
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    private static GccGroundingResolver Build(GccProjectDto? project, IGeekCrawlerRagClient rag, IGccCrawlPageReader? pages = null) =>
        new(new FakeProjects(project), rag, new GccTypedPassageReader(pages ?? new NoPages()),
            new GccPublisherPositionsReader(pages ?? new NoPages(), NullLogger<GccPublisherPositionsReader>.Instance),
            NullLogger<GccGroundingResolver>.Instance);

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task EveryLongFormTypeRetrievesAllThreeCrawlTypes(string type)
    {
        // Three crawls are paid for and three have a purpose: the site so we do not repeat
        // ourselves, partners so what we say about them is true, competitors so the piece is
        // differentiated. Only one of the three was ever fetched, and only for one content type.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag();

        var outcome = await Build(project, rag).ResolveAsync(Create(project.Id, type), type);

        Assert.False(outcome.Refused);
        Assert.Contains(CrawlTypes.ProjectSite, rag.Queried);
        Assert.Contains(CrawlTypes.Partner, rag.Queried);
        Assert.Contains(CrawlTypes.Competitors, rag.Queried);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task A_pages_list_follows_the_corpus_it_came_from_not_the_draft_being_written(string type)
    {
        // Jeff, 2026-10-01: "Blog & Pillar describe solutions, Tool actually Blockquote cites, and
        // project-site is n/a". The three lists carry three different instructions -- cite,
        // differentiate from, do not repeat -- so a page belongs to one because of where it was
        // retrieved from, never because of what is being written.
        //
        // This was order-dependent twice over. crawlTypes used to be mustCite.Concat(retrieveFor),
        // which is per content type, so tool walked Partner-first and pillar ProjectSite-first and
        // a shared dedupe gave one URL different roles per draft. Unioning mustCite made it
        // consistent and wrong the other way: pillar and blog inherited tool's Partner-first
        // precedence, so the publisher's own page could arrive as partner evidence for a pillar.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var outcome = await Build(project, new CrawlTypeRag()).ResolveAsync(Create(project.Id, type), type);

        Assert.False(outcome.Refused);
        Assert.Equal(PartnerUrl, Assert.Single(outcome.Pages).Url);
        Assert.Equal(CompetitorUrl, Assert.Single(outcome.CompetitorPages).Url);
        Assert.Equal(SiteUrl, Assert.Single(outcome.SitePages).Url);
    }

    /// <summary>Returns the SAME url for every crawl type — an operator declaring one URL twice.</summary>
    private sealed class SameUrlEverywhereRag : IGeekCrawlerRagClient
    {
        private const string Shared = "https://shared.test/a";

        public bool IsEnabled => true;

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, string crawlType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerRagHostIndex>>(
                [.. urls.Select(u => new GeekCrawlerRagHostIndex(u, "shared.test", true, Guid.NewGuid().ToString()))]);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagQueryResult?>(new GeekCrawlerRagQueryResult
            {
                RunId = runId,
                Pages = [new GccQuoteablePage(Shared, "Shared", [], ["Body."])],
            });

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

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

        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            Task.FromResult(new GeekCrawlerRagCapabilities());
    }

    [Fact]
    public async Task One_url_in_two_corpora_appears_in_both_lists_rather_than_whichever_was_walked_first()
    {
        // Dedupe is per corpus, not across them. A URL returned by two crawl types was declared
        // twice by the operator; suppressing it from the second list silently assigned it a role by
        // walk order, which is how "partner evidence" and "already on our site" became a function of
        // which draft was being written rather than of where the page came from.
        //
        // Both lists is the honest answer: the prompt blocks render it under both instructions, and
        // the double declaration is visible instead of resolved behind the operator's back.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var outcome = await Build(project, new SameUrlEverywhereRag())
            .ResolveAsync(Create(project.Id, "tool"), ["pillar", "blog", "tool"]);

        Assert.False(outcome.Refused);
        Assert.Single(outcome.Pages);
        Assert.Single(outcome.CompetitorPages);
        Assert.Single(outcome.SitePages);
    }

    [Fact]
    public async Task A_partner_run_is_retrieved_deeper_than_the_prose_corpora()
    {
        // Eight is right for prose and wrong for a product. A tool page is gated on 3 of 22 payload
        // categories being populated, and eight topically-ranked pages cannot span 22 categories -- a
        // live run gave Dext 2 of 22 from 6 pages and Bill, Melio and Stampli 1 of 22 from 5-7, against
        // partners carrying 181-231 crawled pages.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag();

        await Build(project, rag).ResolveAsync(Create(project.Id, "tool"), "tool");

        var partner = Assert.Single(rag.Depths, d => d.CrawlType == CrawlTypes.Partner);
        var site = Assert.Single(rag.Depths, d => d.CrawlType == CrawlTypes.ProjectSite);

        Assert.True(partner.TopK > site.TopK, $"partner {partner.TopK} should exceed site {site.TopK}");
        // 32 is where the next ceiling sits: GccTypedPassageReader reads at most 32 pages back per run,
        // so retrieving more would hand the quote cutter spans with no typed blocks behind them.
        Assert.Equal(32, partner.TopK);
    }

    [Fact]
    public async Task Only_the_partner_corpus_is_read_back_as_typed_blocks()
    {
        // The typed passages are the tool page's quote source, and a tool page must quote a partner:
        // a competitor is read and never quoted, and the publisher's own pages are what the piece
        // must not repeat. So the list carries partner pages only, decided by the crawl type that was
        // queried rather than by whoever cuts the candidates remembering to filter -- the same reason
        // CompetitorPages is its own list instead of a tag on Pages.
        //
        // It is also the cheaper half: the read behind this costs a repository round trip per run,
        // and the competitor and project-site runs were paying it for a list nothing may quote from.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var outcome = await Build(project, new SameUrlEverywhereRag())
            .ResolveAsync(Create(project.Id, "tool"), ["pillar", "blog", "tool"]);

        Assert.False(outcome.Refused);
        Assert.Single(outcome.Pages);
        Assert.Single(outcome.CompetitorPages);
        Assert.Single(outcome.SitePages);
        // One page in three corpora, and exactly one passage: the partner read, and only that one.
        Assert.Single(outcome.PartnerPassages);
    }

    [Fact]
    public async Task Resolving_every_type_at_once_assigns_the_same_roles_as_resolving_one()
    {
        // The single resolve must not change what anything means. Same corpora, same lists.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var alone = await Build(project, new CrawlTypeRag())
            .ResolveAsync(Create(project.Id, "pillar"), "pillar");
        var together = await Build(project, new CrawlTypeRag())
            .ResolveAsync(Create(project.Id, "pillar"), ["pillar", "blog", "tool"]);

        Assert.Equal(alone.Pages.Select(p => p.Url), together.Pages.Select(p => p.Url));
        Assert.Equal(alone.CompetitorPages.Select(p => p.Url), together.CompetitorPages.Select(p => p.Url));
        Assert.Equal(alone.SitePages.Select(p => p.Url), together.SitePages.Select(p => p.Url));
    }

    [Fact]
    public async Task ThreeContentTypesQueryTheCorpusOnce_NotThreeTimes()
    {
        // The defect this closes: grounding was resolved inside the per-type fan-out, so a
        // pillar+blog+tool generate issued the same queries three times over the same runs and
        // discarded two of the answers. On a real project -- 1 site, 10 partner, 10 competitor
        // runs -- that is 63 vector queries where 21 are needed.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var one = new CrawlTypeRag();
        await Build(project, one).ResolveAsync(Create(project.Id, "pillar"), "pillar");

        var three = new CrawlTypeRag();
        var outcome = await Build(project, three)
            .ResolveAsync(Create(project.Id, "pillar"), ["pillar", "blog", "tool"]);

        Assert.False(outcome.Refused);
        Assert.Equal(one.Queried.Count, three.Queried.Count);
    }

    [Fact]
    public async Task OneResolveStillCoversEveryCrawlTypeEveryRequestedTypeNeeds()
    {
        // Cheaper must not mean thinner. The union of what the three types retrieve is still all
        // three crawl types, and tool's must-cite partner requirement survives the union.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag();

        var outcome = await Build(project, rag)
            .ResolveAsync(Create(project.Id, "pillar"), ["pillar", "blog", "tool"]);

        Assert.False(outcome.Refused);
        Assert.Contains(CrawlTypes.ProjectSite, rag.Queried);
        Assert.Contains(CrawlTypes.Partner, rag.Queried);
        Assert.Contains(CrawlTypes.Competitors, rag.Queried);
        Assert.NotEmpty(outcome.Pages);
        Assert.NotEmpty(outcome.CompetitorPages);
        Assert.NotEmpty(outcome.SitePages);
    }

    [Fact]
    public async Task AMultiTypeGenerateIncludingToolStillRefusesWhenPartnerEvidenceIsMissing()
    {
        // tool's must-cite requirement is unioned in, so it refuses the whole generate, before any
        // type is attempted, rather than being silently dropped because pillar and blog would have
        // been content without it.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag(partnerIndexed: false);

        var outcome = await Build(project, rag)
            .ResolveAsync(Create(project.Id, "pillar"), ["pillar", "blog", "tool"]);

        Assert.True(outcome.Refused);
    }

    [Fact]
    public async Task AMissingCrawlRefusesEveryTypeEvenOneThatCitesNothing()
    {
        // Pins what the code does, which is not what the code says. RetrieveCrawlTypes' own remark
        // claims "Retrieval failure is a refusal only where MustCiteCrawlTypes says so. A pillar
        // whose competitor crawl is missing is a thinner pillar, not a draft that must not exist."
        // The refusal at GccGroundingResolver.cs:273-279 fires per crawl type whenever no indexed
        // run is found, and never consults mustCite -- so pillar and blog, which must cite nothing,
        // are refused for a missing partner crawl.
        //
        // Pre-existing and left alone deliberately: changing it is a policy call about when a
        // draft may be thinner rather than refused, and this change set is about resolving the
        // same evidence once. Recorded here so the next reader finds the contradiction pinned
        // rather than trusts the remark.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag(partnerIndexed: false);

        var outcome = await Build(project, rag)
            .ResolveAsync(Create(project.Id, "pillar"), ["pillar", "blog"]);

        Assert.True(outcome.Refused);
    }

    [Fact]
    public async Task AiToolIsTheSameTypeAsToolAndGetsOneRowNotTwo()
    {
        // "aiTool" is the picker's spelling. Listing it as a second key was a second row for one
        // type, which is the shape that lets two spellings of one thing drift apart.
        Assert.Equal(GccGroundingResolver.RetrieveFor("tool"), GccGroundingResolver.RetrieveFor("aiTool"));
        Assert.Equal(GccGroundingResolver.RequiredFor("tool"), GccGroundingResolver.RequiredFor("aiTool"));
    }

    [Fact]
    public async Task OwnSitePagesLandInTheirOwnListAndTheBlockForbidsRepeatingThem()
    {
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var outcome = await Build(project, new CrawlTypeRag())
            .ResolveAsync(Create(project.Id), "pillar");

        Assert.Equal(SiteUrl, Assert.Single(outcome.SitePages).Url);

        var research = new GccResearchDocument(null, [], SiteQuoteables: outcome.SitePages);
        var create = Create(Guid.NewGuid()) with { ResearchJson = GccResearchFetchService.Serialize(research) };
        var block = GccGenerateService.BuildOwnSiteCoverageBlock(create, []);

        Assert.Contains("do not write these again", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We published this last quarter.", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The site's own page about a tool that is not this project's partner -- its tool page for a
    /// partner of another project -- is left out of what the writer reads, and the name is redacted
    /// where another own page mentions it. The 8:26 run of 2026-10-06 named Tipalti, a partner on
    /// another Accounts Payable project, because the site's Tipalti page was printed here beside the
    /// instruction not to name it.
    /// </summary>
    [Fact]
    public void An_own_site_page_about_an_unlisted_tool_is_left_out_and_the_name_is_redacted_elsewhere()
    {
        var tipaltiPage = new GccQuoteablePage(
            "https://geekatyourspot.com/tools/accounting/accounts-payable/tipalti",
            "Tipalti: Automated Invoice Processing",
            [new HeadingDto(1, "Tipalti for AP")],
            ["Tipalti automates supplier payments."]);
        var overview = new GccQuoteablePage(
            "https://geekatyourspot.com/accounting/accounts-payable",
            "Accounts Payable automation",
            [new HeadingDto(2, "Tools we implement, Tipalti included")],
            ["We published this last quarter. Clients on Tipalti and Ramp both saw it."]);
        var research = new GccResearchDocument(null, [], SiteQuoteables: [tipaltiPage, overview]);
        var create = Create(Guid.NewGuid()) with { ResearchJson = GccResearchFetchService.Serialize(research) };

        var block = GccGenerateService.BuildOwnSiteCoverageBlock(create, ["Tipalti", "Melio"]);

        Assert.DoesNotContain("accounts-payable/tipalti", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Automated Invoice Processing", block, StringComparison.Ordinal);
        Assert.Contains("Accounts Payable automation", block, StringComparison.Ordinal);
        Assert.Contains("Tools we implement, another tool included", block, StringComparison.Ordinal);
        Assert.Contains("Clients on another tool and Ramp both saw it.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("tipalti", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProjectWithNoSiteCrawlIsRefused()
    {
        // ProjectForm refuses to create a project without an indexed site crawl, so reaching here
        // means the crawl was dropped or the project pre-dates that gate. A fault, not a thin draft.
        var project = Project([PartnerUrl], [CompetitorUrl]) with { ProjectSiteRunId = null };

        var outcome = await Build(project, new CrawlTypeRag())
            .ResolveAsync(Create(project.Id), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("project-site", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartnerAndCompetitorPagesStayInSeparateLists()
    {
        // A rival rendered under the partner block would be cited by name and URL, because that
        // block's Rule 2 says to attribute every passage. Keeping them apart is what prevents it.
        var project = Project([PartnerUrl], [CompetitorUrl]);

        var outcome = await Build(project, new CrawlTypeRag())
            .ResolveAsync(Create(project.Id), "pillar");

        Assert.Equal(PartnerUrl, Assert.Single(outcome.Pages).Url);
        Assert.Equal(CompetitorUrl, Assert.Single(outcome.CompetitorPages).Url);
    }

    [Fact]
    public async Task ADeclaredCompetitorUrlWithNoIndexedCrawlIsRefused()
    {
        // Declared URLs are index-checked before a project may be saved, so an unindexed one at
        // generate time is a dropped crawl, not a normal state to write around.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag(competitorIndexed: false);

        var outcome = await Build(project, rag).ResolveAsync(Create(project.Id), "pillar");

        Assert.True(outcome.Refused);
        Assert.Contains("competitors", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AToolWithNoIndexedPartnerCrawlIsStillRefused()
    {
        // The one fail-closed gate that already worked. Widening retrieval must not weaken it.
        var project = Project([PartnerUrl], [CompetitorUrl]);
        var rag = new CrawlTypeRag(partnerIndexed: false);

        var outcome = await Build(project, rag).ResolveAsync(Create(project.Id, "tool"), "tool");

        Assert.True(outcome.Refused);
        Assert.Contains("indexed crawl", outcome.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APillarOnACreateWithNoProjectRetrievesNothingAndIsNotRefused()
    {
        var outcome = await Build(null, new CrawlTypeRag()).ResolveAsync(Create(null), "pillar");

        Assert.False(outcome.Refused);
        Assert.Empty(outcome.Pages);
        Assert.Empty(outcome.CompetitorPages);
    }

    [Fact]
    public void TheCompetitorBlockForbidsQuotingCitingAndLinking()
    {
        var research = new GccResearchDocument(
            null,
            [],
            CompetitorQuoteables:
            [
                new GccQuoteablePage(
                    CompetitorUrl, "Rival services",
                    [new HeadingDto(2, "What we do")],
                    ["We run AP projects end to end."]),
            ]);
        var create = Create(Guid.NewGuid()) with { ResearchJson = GccResearchFetchService.Serialize(research) };

        var block = GccGenerateService.BuildCompetitorResearchBlock(create);

        Assert.Contains("never quote, cite or link", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We run AP projects end to end.", block, StringComparison.Ordinal);
        // The rival's URL is deliberately absent: a URL in the prompt is a URL that can be written.
        Assert.DoesNotContain(CompetitorUrl, block, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePartnerBlockNeverCarriesACompetitorPage()
    {
        var research = new GccResearchDocument(
            null,
            [new GccQuoteablePage(PartnerUrl, "Partner pricing", [], ["Billed per document."])],
            CompetitorQuoteables:
            [new GccQuoteablePage(CompetitorUrl, "Rival services", [], ["We run AP projects."])]);
        var create = Create(Guid.NewGuid()) with { ResearchJson = GccResearchFetchService.Serialize(research) };

        var partnerBlock = GccGenerateService.BuildResearchBlock(create);

        Assert.Contains("Billed per document.", partnerBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Rival services", partnerBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(CompetitorUrl, partnerBlock, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rule 2 of the research block names each page by the id a "links" entry uses, and tells the writer
    /// it writes no URL. It used to say "include its URL where the claim appears" (the writer typed
    /// "[Source: title](url)" into a run, 2026-10-03), then to carry the URL as a short run's "href"
    /// (the writer put it on whole paragraphs, 2026-10-05 and twice on 2026-10-09). The prompt, the
    /// placer and the guard are one rule in three places, and this pins the half the writer reads.
    /// </summary>
    [Fact]
    public void ThePartnerBlockNamesEachPageByIdAndTheWriterWritesNoUrl()
    {
        var research = new GccResearchDocument(
            null,
            [new GccQuoteablePage(PartnerUrl, "Partner pricing", [], ["Billed per document."])]);
        var create = Create(Guid.NewGuid()) with { ResearchJson = GccResearchFetchService.Serialize(research) };

        var block = GccGenerateService.BuildResearchBlock(create);

        Assert.Contains($"[S1] Partner pricing ({PartnerUrl})", block, StringComparison.Ordinal);
        Assert.Contains("\"target\" is that page's S# id", block, StringComparison.Ordinal);
        Assert.Contains("You write no URL anywhere", block, StringComparison.Ordinal);
        Assert.DoesNotContain("run's \"href\"", block, StringComparison.Ordinal);
        Assert.DoesNotContain("include its URL where the claim appears", block, StringComparison.Ordinal);
        // No quotation is licensed here: pillar and blog ban block quotes and run no quote guard,
        // so a Rule 2 that offered "the cite of a quote paragraph" invited an unverified one.
        Assert.DoesNotContain("quote paragraph", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("600-850 words", "550-750 words", 1150)]
    [InlineData("1,200-1,500 words", "450-600 words", 1650)]
    public void A_batch_owes_the_sum_of_its_slots_lower_figures(string first, string second, int expected)
    {
        var batch = new[]
        {
            GeekAPI.Services.Workflow.Services.PromptBuilders.SectionSlot.Cover("one", first),
            GeekAPI.Services.Workflow.Services.PromptBuilders.SectionSlot.Cover("two", second),
        };

        Assert.Equal(expected, GccGenerateService.BatchFloorWords(batch));
    }

    [Fact]
    public void A_batch_with_an_unsized_slot_owes_nothing_measurable()
    {
        // A floor derived from half the slots would be a guess about the other half.
        var batch = new[]
        {
            GeekAPI.Services.Workflow.Services.PromptBuilders.SectionSlot.Cover("one", "600-850 words"),
            GeekAPI.Services.Workflow.Services.PromptBuilders.SectionSlot.Cover("two"),
        };

        Assert.Equal(0, GccGenerateService.BatchFloorWords(batch));
    }

    [Fact]
    public void Metadata_missing_a_required_field_is_refused_by_name()
    {
        var draft = new GeekAPI.Services.Workflow.DTOs.BlogMetadataDraft("Title", null!, ["kw"], ["One"]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GccGenerateService.RequireCompleteMetadata(draft, "blog metadata"));

        Assert.Contains("without \"metaDescription\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_warnings_a_generator_wrote_into_its_envelope_are_read_back_by_name()
    {
        // A pillar naming four of five partners is saved with the gap recorded, not refused; this is
        // how the gap reaches the workspace.
        var envelope = """{"title":"T","warnings":["Pillar names 4 of 5 partner tools. Missing: Ramp.",""],"body":{"lede":null,"sections":[]}}""";

        var warnings = GccGenerationCoordinator.WarningsOf(envelope);

        var only = Assert.Single(warnings);
        Assert.StartsWith("Pillar names 4 of 5", only, StringComparison.Ordinal);
        Assert.Empty(GccGenerationCoordinator.WarningsOf("""{"title":"T","body":{}}"""));
        Assert.Empty(GccGenerationCoordinator.WarningsOf("""{"lede":null,"sections":[]}"""));
        Assert.Empty(GccGenerationCoordinator.WarningsOf("not json"));
    }

    [Fact]
    public void Metadata_with_an_optional_field_absent_is_complete()
    {
        var draft = new GeekAPI.Services.Workflow.DTOs.BlogMetadataDraft("Title", "Meta", ["kw"], ["One"], Summary: null);

        Assert.Same(draft, GccGenerateService.RequireCompleteMetadata(draft, "blog metadata"));
    }
}
