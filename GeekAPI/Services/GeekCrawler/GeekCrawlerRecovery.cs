using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerRecovery
{
    public static readonly TimeSpan PendingWakeGrace = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StalledRunningGrace = TimeSpan.FromMinutes(5);

    public static bool ShouldWakeAtStartup(GeekCrawlerRunDto run, DateTimeOffset now) =>
        string.Equals(run.Status, "pending", StringComparison.OrdinalIgnoreCase)
        && now - run.CreatedAtUtc >= PendingWakeGrace;

    /// <summary>
    /// A deploy restart can mark a run <c>running</c> then kill the worker before any pages are saved.
    /// </summary>
    public static bool ShouldRecoverRunningOrphan(
        GeekCrawlerRunDto run,
        DateTimeOffset now,
        bool hasSavedPages) =>
        string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase)
        && !hasSavedPages
        && run.StartedAtUtc is not null
        && now - run.StartedAtUtc.Value >= PendingWakeGrace;

    /// <summary>
    /// A deploy restart can kill the worker after pages were saved but before BFS completes.
    /// </summary>
    public static bool ShouldRecoverStalledRunning(
        GeekCrawlerRunDto run,
        DateTimeOffset now,
        DateTimeOffset lastPageCrawledAtUtc) =>
        string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase)
        && now - lastPageCrawledAtUtc >= StalledRunningGrace;
}
