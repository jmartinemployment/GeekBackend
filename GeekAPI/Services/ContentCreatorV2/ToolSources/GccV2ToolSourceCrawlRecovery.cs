using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

/// <summary>
/// One-shot recovery for tool-source crawl runs whose in-memory wake was lost (API restart, missed channel write).
/// </summary>
public static class GccV2ToolSourceCrawlRecovery
{
    public static readonly TimeSpan PendingWakeGrace = TimeSpan.FromSeconds(30);

    public static bool ShouldWakeAtStartup(GccV2ToolSourceCrawlRunDto run, DateTimeOffset now) =>
        string.Equals(run.Status, "pending", StringComparison.OrdinalIgnoreCase)
        && now - run.CreatedAtUtc >= PendingWakeGrace;
}
