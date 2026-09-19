namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string CrawlType { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string SeedUrlsJson { get; set; } = "[]";
    public string? SeedKey { get; set; }
    public string? HostProgressJson { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    /** Set only when every persisted usable page has extracted content. */
    public DateTimeOffset? ContentReadyAt { get; set; }

    /**
     * The crawl's completion report: what was stored, what was excluded by policy, what failed and
     * why. Written once at commit or abort. Null on a run that never reached either.
     */
    public string? CrawlReportJson { get; set; }

    /**
     * Geek-Crawler-Rag index status, mirrored from its index-status webhook. Null on a run that
     * was never (yet) indexed. Written only via UpdateRagIndexStatusAsync's atomic $set -- never
     * through UpdateRunAsync's read-modify-replace, which would race a concurrent crawl-progress
     * write on this same document.
     */
    public string? RagState { get; set; }
    public int? RagChunksUpserted { get; set; }
    public int? RagPagesEnglish { get; set; }
    public DateTimeOffset? RagIndexedAtUtc { get; set; }
}
