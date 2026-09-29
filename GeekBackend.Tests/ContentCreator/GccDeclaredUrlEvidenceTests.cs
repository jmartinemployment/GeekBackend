using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Indexed is not usable.
///
/// <para>
/// A crawl can complete having been blocked at its first page, or against a site that renders
/// nothing without JavaScript, and still put a row in the index. That passes "does an index exist"
/// -- the only question asked until now -- and hands the writer nothing. The run records what
/// actually landed, and <c>RagPagesEnglish</c> / <c>RagChunksUpserted</c> were written by the RAG
/// webhook and read by nothing.
/// </para>
/// </summary>
public class GccDeclaredUrlEvidenceTests
{
    private const string Url = "https://partner.test";

    private static GeekCrawlerRagHostIndex Row(bool indexed = true, string? runId = null) =>
        new(Url, "partner.test", indexed, runId ?? Guid.NewGuid().ToString());

    private static GeekCrawlerRunDto Run(
        string status = "complete",
        DateTimeOffset? contentReadyAt = null,
        int? pages = 40,
        int? chunks = 400) =>
        new(
            Id: Guid.NewGuid(), OwnerUserId: "operator", CrawlType: "partner", Status: status,
            SeedUrlsJson: "[]", SeedKey: null, HostProgressJson: null, ErrorSummary: null,
            CreatedAtUtc: DateTimeOffset.UtcNow, StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            ContentReadyAt: contentReadyAt ?? DateTimeOffset.UtcNow,
            CrawlReportJson: null, RagState: "indexed",
            RagChunksUpserted: chunks, RagPagesEnglish: pages,
            RagIndexedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void ACompleteCrawlWithRealVolumeIsUsable() =>
        Assert.Null(GccDeclaredUrlEvidence.Unusable(Row(), Run()));

    [Fact]
    public void NotIndexedIsUnusable() =>
        Assert.Equal(
            "no crawl is indexed for it",
            GccDeclaredUrlEvidence.Unusable(Row(indexed: false), null));

    [Fact]
    public void AnIndexRowNamingARunThatCannotBeReadIsUnusable() =>
        Assert.Equal(
            "the index names a crawl run that cannot be read",
            GccDeclaredUrlEvidence.Unusable(Row(), null));

    [Fact]
    public void ACrawlStillRunningIsUnusable() =>
        Assert.Equal(
            "its crawl is running, not complete",
            GccDeclaredUrlEvidence.Unusable(Row(), Run(status: "running")));

    [Fact]
    public void ACompleteCrawlThatExtractedNoContentIsUnusable()
    {
        var run = Run() with { ContentReadyAt = null };

        Assert.Equal(
            "its crawl extracted no content",
            GccDeclaredUrlEvidence.Unusable(Row(), run));
    }

    [Fact]
    public void ABlockedCrawlIsIndexedAndStillUnusable()
    {
        // The case this whole check exists for: a row in the index, a completed run, and one thin
        // page behind it. Every earlier version of this gate passed it.
        var reason = GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 1, chunks: 3));

        Assert.NotNull(reason);
        Assert.Contains("1 page(s) and 3 chunk(s)", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrochureSiteIsUnusable()
    {
        // Eight pages and eighty chunks is a real crawl of a small marketing site, and still not
        // something a 3,000-word partner page can be paraphrased out of. The first floor here was
        // 5 pages and 25 chunks, which passed this.
        Assert.NotNull(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 8, chunks: 80)));
    }

    [Fact]
    public void TheFloorIsPagesAndChunksTogether()
    {
        // Plenty of chunks from one page is one page re-chunked, not a corpus.
        Assert.NotNull(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 1, chunks: 4000)));
        // Plenty of pages yielding almost nothing is a crawl that fetched a site rendering nothing
        // without JavaScript: 40 pages at 2 chunks each is 40 nav shells.
        Assert.NotNull(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 40, chunks: 80)));
    }

    [Fact]
    public void TwentyFiveRealPagesIsUsable()
    {
        // At ~10 chunks per substantial page, this is what a genuine vendor or consultancy site
        // looks like once crawled.
        Assert.Null(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 25, chunks: 250)));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    public void TheCountRuleNamesWhatIsMissing(int actual, int required) =>
        Assert.Equal(
            $"Partner URLs: {actual} declared, {required} required.",
            GccDeclaredUrlEvidence.WrongCount("Partner URLs", actual, required));

    [Fact]
    public void MeetingTheCountSaysNothing() =>
        Assert.Null(GccDeclaredUrlEvidence.WrongCount("Partner URLs", 5, 5));

    [Fact]
    public void MoreThanRequiredIsFine() =>
        // "There must be 5" is a floor. Refusing a sixth partner would be arbitrary.
        Assert.Null(GccDeclaredUrlEvidence.WrongCount("Partner URLs", 6, 5));
}
