namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerRunStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    /// <summary>Owned by an external crawler (Crawlee); ignored by GeekCrawlerWorker.</summary>
    public const string External = "external";
    public const string Complete = "complete";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>
    /// A superseded run whose vectors could not be purged from Qdrant. It published
    /// successfully once and has since been replaced; what is outstanding is the cleanup of
    /// the index it left behind, not anything about the crawl.
    /// </summary>
    /// <remarks>
    /// Exists so that failure is not borrowed from one run to describe another. A purge
    /// failure used to be reported against the run that had just published — the PATCH
    /// returned 502 while the run was recorded complete — which told the operator the crawl
    /// had failed when it had not. Eleven runs on 24–25 September 2026 read as failed in the
    /// crawler UI while holding 42,603, 21,488, 10,956 and 9,655 chunks of a healthy,
    /// fully-indexed corpus.
    ///
    /// Not <c>failed</c>, because nothing about this run failed. Not <c>complete</c>, because
    /// something is genuinely outstanding and crawling must stay blocked until it is done:
    /// orphaned vectors are reachable, since every retrieval is filtered to one runId and
    /// /v1/index/hosts resolves a host to a runId by scanning Qdrant on host alone. An
    /// explicit awaiting_* state is what the job rules require instead of either.
    /// </remarks>
    public const string AwaitingVectorPurge = "awaiting_vector_purge";

    public static bool IsInProgress(string? status) =>
        string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Running, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, External, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Statuses that block every new crawl for this owner until resolved: a discard that did
    /// not complete, or a superseded run whose vectors survive. Both mean Qdrant and the
    /// corpus may disagree, which is not confined to one site — an index the system cannot
    /// delete from is unhealthy for every crawl.
    /// </summary>
    public static bool BlocksCrawling(string? status) =>
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, AwaitingVectorPurge, StringComparison.OrdinalIgnoreCase);
}
