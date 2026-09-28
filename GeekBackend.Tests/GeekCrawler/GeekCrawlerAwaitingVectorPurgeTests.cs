using GeekAPI.Services.GeekCrawler;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// A superseded run's unpurged vectors are that run's problem, not the published run's.
///
/// Eleven crawls on 24–25 September 2026 were recorded failed while holding a fully indexed
/// corpus — 42,603 chunks for highradius.com, 21,488 for medius.com, 10,956 for eojohnson.com,
/// 9,655 for avidxchange.com. Each had published successfully; what failed was purging a
/// *different*, superseded run's points from Qdrant, and the commit path expressed that by
/// returning 502 on the PATCH that had just recorded the new run complete. The operator could not
/// tell "this crawl is bad" from "an unrelated cleanup is pending".
///
/// The halt itself was right and stays: orphaned vectors are reachable, because every retrieval is
/// filtered to exactly one runId and /v1/index/hosts resolves a host to a runId by scanning Qdrant
/// on host alone, so a stale run's points can be selected as a host's grounding evidence. What
/// changed is which run carries the condition, and that enforcement lives in the crawl gate rather
/// than in a status code attached to the wrong run.
/// </summary>
public sealed class GeekCrawlerAwaitingVectorPurgeTests
{
    [Fact]
    public void Awaiting_vector_purge_is_its_own_status_and_not_a_failure()
    {
        Assert.Equal("awaiting_vector_purge", GeekCrawlerRunStatuses.AwaitingVectorPurge);

        // Distinct from both neighbours it would otherwise be collapsed into.
        Assert.NotEqual(GeekCrawlerRunStatuses.Failed, GeekCrawlerRunStatuses.AwaitingVectorPurge);
        Assert.NotEqual(GeekCrawlerRunStatuses.Complete, GeekCrawlerRunStatuses.AwaitingVectorPurge);
    }

    [Fact]
    public void Awaiting_vector_purge_is_not_in_progress()
    {
        // Nothing is crawling. Counting it as in-progress would make the owner look busy and
        // suppress the very crawl whose gate is meant to retry the purge.
        Assert.False(GeekCrawlerRunStatuses.IsInProgress(GeekCrawlerRunStatuses.AwaitingVectorPurge));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("awaiting_vector_purge")]
    [InlineData("FAILED")]
    [InlineData("Awaiting_Vector_Purge")]
    public void Statuses_that_block_crawling_are_recognised(string status)
    {
        // The new state must block crawling exactly as a stuck discard does. If it did not, the
        // status would be operator-visible but unenforced — a safety property documented and never
        // checked, which is how ingest came to accept pages it silently discarded.
        Assert.True(GeekCrawlerRunStatuses.BlocksCrawling(status));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("pending")]
    [InlineData("running")]
    [InlineData("external")]
    [InlineData(null)]
    [InlineData("")]
    public void Healthy_and_in_flight_statuses_do_not_block_crawling(string? status)
    {
        // Especially "complete": the published run must never be what blocks the next crawl. That
        // conflation is the defect these tests exist for.
        Assert.False(GeekCrawlerRunStatuses.BlocksCrawling(status));
    }
}
