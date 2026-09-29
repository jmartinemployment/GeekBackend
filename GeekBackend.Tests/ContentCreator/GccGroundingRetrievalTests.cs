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

        public bool IsEnabled => true;

        public Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
            IReadOnlyList<string> urls, CancellationToken ct = default)
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
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    private static GccGroundingResolver Build(GccProjectDto? project, IGeekCrawlerRagClient rag) =>
        new(new FakeProjects(project), rag, new NoPages(),
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
        var block = GccGenerateService.BuildOwnSiteCoverageBlock(create);

        Assert.Contains("do not write these again", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We published this last quarter.", block, StringComparison.Ordinal);
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
}
