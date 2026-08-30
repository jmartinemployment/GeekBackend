using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerRecovery
{
    public static readonly TimeSpan PendingWakeGrace = TimeSpan.FromSeconds(30);

    public static bool ShouldWakeAtStartup(GeekCrawlerRunDto run, DateTimeOffset now) =>
        string.Equals(run.Status, "pending", StringComparison.OrdinalIgnoreCase)
        && now - run.CreatedAtUtc >= PendingWakeGrace;
}
