using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Hosting;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerOptions
{
    public const int MaxParallelismPerOrigin = 8;

    public int WorkerCount { get; init; } = 1;
    public string Mode { get; init; } = "standard";
    public int ParallelismPerOrigin { get; init; } = 1;
    public int HostDelaySeconds { get; init; } = 12;
    public int BatchSaveSize { get; init; } = GeekCrawlerCaps.BatchSaveSize;
    public bool SeedsOnly { get; init; }
    /// <summary>
    /// When true, startup pending recovery only wakes runs with zero saved pages.
    /// Used on the local Mac so soft-site resumes stay on Railway.
    /// </summary>
    public bool WakeZeroPagePendingOnly { get; init; }

    public static GeekCrawlerOptions FromConfiguration(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var mode = configuration["GEEK_CRAWLER_MODE"]?.Trim().ToLowerInvariant() ?? "standard";
        var accelerated = string.Equals(mode, "accelerated", StringComparison.Ordinal);

        // 0 is intentional: idle this GeekAPI instance's crawl workers (local Mac / Railway handoff).
        var workerCount = ParseMinInt(
            configuration["GEEK_CRAWLER_WORKER_COUNT"],
            defaultValue: 1,
            min: 0);

        var defaultParallelism = accelerated ? 4 : 1;
        var defaultDelay = accelerated ? 3 : 12;

        var seedsOnly = ParseBool(configuration["GEEK_CRAWLER_SEEDS_ONLY"]);
        if (seedsOnly && environment is not null && !environment.IsDevelopment())
            seedsOnly = false;

        var wakeZeroOnly = ParseBool(configuration["GEEK_CRAWLER_WAKE_ZERO_PAGE_PENDING_ONLY"]);
        if (!wakeZeroOnly && environment is not null && environment.IsDevelopment())
            wakeZeroOnly = true;

        return new GeekCrawlerOptions
        {
            WorkerCount = workerCount,
            Mode = accelerated ? "accelerated" : "standard",
            ParallelismPerOrigin = ParseBoundedInt(
                configuration["GEEK_CRAWLER_PARALLELISM_PER_ORIGIN"],
                defaultValue: defaultParallelism,
                min: 1,
                max: MaxParallelismPerOrigin),
            HostDelaySeconds = ParseBoundedInt(
                configuration["GEEK_CRAWLER_HOST_DELAY_SECONDS"],
                defaultValue: defaultDelay,
                min: 0,
                max: 120),
            BatchSaveSize = ParseBoundedInt(
                configuration["GEEK_CRAWLER_BATCH_SAVE_SIZE"],
                defaultValue: GeekCrawlerCaps.BatchSaveSize,
                min: 1,
                max: GeekCrawlerCaps.MaxBatchSaveSize),
            SeedsOnly = seedsOnly,
            WakeZeroPagePendingOnly = wakeZeroOnly,
        };
    }

    private static bool ParseBool(string? raw) =>
        string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw?.Trim(), "1", StringComparison.Ordinal);

    private static int ParseMinInt(string? raw, int defaultValue, int min)
    {
        if (!int.TryParse(raw, out var value))
            return defaultValue;
        return Math.Max(min, value);
    }

    private static int ParseBoundedInt(string? raw, int defaultValue, int min, int max)
    {
        if (!int.TryParse(raw, out var value))
            return defaultValue;
        return Math.Clamp(value, min, max);
    }
}
