using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// plans/rag-foundation-rewrite.md Verification: "every extracted asset still carries a verified quote
/// with offsets".
///
/// Extraction records the verbatim span it used; verify checks that span against source Markdown and
/// stamps offsets, digest and rights. This is the whole basis for naming a third party in published
/// output, so it is covered end to end rather than by asserting the provenance fields merely exist.
/// </summary>
public sealed class GccV2ExtractionQuoteVerifyTests
{
    private const string PageId = "page-1";
    private const string RunId = "run-1";
    private const string Markdown =
        "# Rival Consulting\n\nWe work exclusively with enterprise clients.\n\nOur team is based in Leeds.\n";

    private sealed class MarkdownRagClient : IGeekCrawlerRagClient
    {
        public bool IsEnabled => true;

        public Task<GeekCrawlerRagPageMarkdown?> GetPageMarkdownAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            Task.FromResult<GeekCrawlerRagPageMarkdown?>(new()
            {
                PageId = pageId,
                RunId = runId ?? RunId,
                Url = "https://rival.example",
                Markdown = Markdown,
            });

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);
        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null, IReadOnlyList<string>? entityNames = null,
            string? retrievalMode = null, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagQueryResult?>(null);
        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateIndexResult?>(null);
        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateQueryResult?>(null);
        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            throw new CapabilitiesUnavailableException("not used");
    }

    private static GccCompetitorExtractionProvenance Prov(string quote) =>
        new("https://rival.example", GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            RunId: RunId, PageId: PageId, SectionTitle: null, SourceDigest: null,
            TemporalAnchorUtc: null, Quote: quote);

    private static GccCompetitorExtractionDocument WithBoundary(string quote) =>
        GccV2CompetitorExtractionService.EmptyDocument() with
        {
            Disqualifiers =
            [
                new GccCompetitorBoundaryAsset(
                    "audience", "Enterprise clients only", "https://rival.example", Prov(quote)),
            ],
        };

    [Fact]
    public async Task A_verbatim_quote_is_stamped_verified_with_offsets_and_a_digest()
    {
        const string quote = "We work exclusively with enterprise clients.";

        var verified = await GccV2CompetitorExtractionVerify.VerifyAgainstLibraryAsync(
            WithBoundary(quote), new MarkdownRagClient(), rawBriefJson: null, CancellationToken.None);

        var provenance = Assert.Single(verified.Disqualifiers).Provenance;

        Assert.True(provenance.MarkdownVerified);
        Assert.Equal(quote, provenance.Quote);
        Assert.NotNull(provenance.StartChar);
        Assert.NotNull(provenance.EndChar);
        Assert.Equal(Markdown.IndexOf(quote, StringComparison.Ordinal), provenance.StartChar);
        Assert.Equal(provenance.StartChar + quote.Length, provenance.EndChar);
        Assert.False(string.IsNullOrWhiteSpace(provenance.SourceDigest));
    }

    [Fact]
    public async Task A_quote_absent_from_source_is_stamped_unverified_and_carries_no_offsets()
    {
        // The failure this guards: asserting a boundary the rival never stated.
        var verified = await GccV2CompetitorExtractionVerify.VerifyAgainstLibraryAsync(
            WithBoundary("They do not serve small business."),
            new MarkdownRagClient(), rawBriefJson: null, CancellationToken.None);

        var provenance = Assert.Single(verified.Disqualifiers).Provenance;

        Assert.False(provenance.MarkdownVerified);
        Assert.Null(provenance.StartChar);
        Assert.Null(provenance.EndChar);
    }

    [Fact]
    public async Task Crawl_type_stays_competitors_through_verification()
    {
        var verified = await GccV2CompetitorExtractionVerify.VerifyAgainstLibraryAsync(
            WithBoundary("Our team is based in Leeds."),
            new MarkdownRagClient(), rawBriefJson: null, CancellationToken.None);

        Assert.Equal(
            GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            Assert.Single(verified.Disqualifiers).Provenance.CrawlType);
    }
}
