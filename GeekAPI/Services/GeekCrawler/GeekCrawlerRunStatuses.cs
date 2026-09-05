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

    public static bool IsInProgress(string? status) =>
        string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Running, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, External, StringComparison.OrdinalIgnoreCase);
}
