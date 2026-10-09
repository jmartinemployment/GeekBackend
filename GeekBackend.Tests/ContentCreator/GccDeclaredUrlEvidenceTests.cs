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
        int? chunks = 400,
        string? ragState = "complete") =>
        new(
            Id: Guid.NewGuid(), OwnerUserId: "operator", CrawlType: "partner", Status: status,
            SeedUrlsJson: "[]", SeedKey: null, HostProgressJson: null, ErrorSummary: null,
            CreatedAtUtc: DateTimeOffset.UtcNow, StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            ContentReadyAt: contentReadyAt ?? DateTimeOffset.UtcNow,
            CrawlReportJson: null, RagState: ragState,
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
    public void ARunStillBeingIndexedIsNamedAsSuchNotAsEmpty()
    {
        // medius.com, 2026-10-09: a first index lands on the run as RagState=running with both
        // counters at zero while the index already holds its first flushes. For the twelve minutes
        // its 424 pages took, the counters called it "indexed 0 page(s) and 0 chunk(s)".
        var reason = GccDeclaredUrlEvidence.Unusable(Row(), Run(ragState: "running", pages: 0, chunks: 0));

        Assert.NotNull(reason);
        Assert.Contains("still being indexed", reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("0 page(s)", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AQueuedIndexIsStillBeingIndexedToo() =>
        Assert.Contains(
            "still being indexed",
            GccDeclaredUrlEvidence.Unusable(Row(), Run(ragState: "pending", pages: 0, chunks: 0))!,
            StringComparison.Ordinal);

    [Fact]
    public void AFinishedIndexThatLandedNothingIsRefusedOnTheCounters()
    {
        // The same zeros with the index finished are a real answer, and the counters give it.
        var reason = GccDeclaredUrlEvidence.Unusable(Row(), Run(ragState: "complete", pages: 0, chunks: 0));

        Assert.NotNull(reason);
        Assert.Contains("0 page(s) and 0 chunk(s)", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunNeverReportedOnIsJudgedByItsCounters() =>
        // Older runs carry no RagState at all. Nothing to wait for; the counters decide.
        Assert.Null(GccDeclaredUrlEvidence.Unusable(Row(), Run(ragState: null)));

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
        // 5 pages and 25 chunks, which passed this. Eight still fails the page floor at ten.
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
        // What a genuine vendor or consultancy site looks like once crawled. Kept at the numbers it
        // was written with, because the recalibration to 10 pages / 3 chunks per page must not move
        // the verdict on a case that was already right.
        Assert.Null(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 25, chunks: 250)));
    }

    [Fact]
    public void ARealSiteJustUnderTheOldPageFloorIsUsable()
    {
        // lightyear.cloud, read off the live index on 2026-10-01: 24 pages, 370 chunks, and its
        // counts confirmed final while the collection as a whole grew 62,343 -> 105,737 points. The
        // old floor of 25 pages refused a genuine vendor site for being one page short, which is
        // what the recalibration exists to stop. This is the regression test for it.
        Assert.Null(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 24, chunks: 370)));
    }

    [Fact]
    public void ThinProseIsRefusedAtEverySiteSize()
    {
        // The reason the second rule is a ratio and not a count. At two chunks a page these are nav
        // shells whether there are forty of them or four hundred; an absolute chunk floor passes the
        // larger one purely for being large. 250 chunks would have admitted the second of these.
        Assert.NotNull(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 40, chunks: 80)));
        Assert.NotNull(GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 400, chunks: 800)));
    }

    [Fact]
    public void TheRefusalNamesBothRules()
    {
        var reason = GccDeclaredUrlEvidence.Unusable(Row(), Run(pages: 40, chunks: 80));

        Assert.NotNull(reason);
        Assert.Contains("40 page(s) and 80 chunk(s)", reason!, StringComparison.Ordinal);
        Assert.Contains(
            $"{GccDeclaredUrlEvidence.MinIndexedPages} pages and "
                + $"{GccDeclaredUrlEvidence.MinChunksPerPage} chunks per page",
            reason!,
            StringComparison.Ordinal);
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
