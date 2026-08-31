namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerOptions
{
    public const int MaxParallelismPerOrigin = 8;

    public int WorkerCount { get; init; } = 1;
    public string Mode { get; init; } = "standard";
    public int ParallelismPerOrigin { get; init; } = 1;
    public int HostDelaySeconds { get; init; } = 12;
    public bool SeedsOnly { get; init; }

    public static GeekCrawlerOptions FromConfiguration(IConfiguration configuration)
    {
        var mode = configuration["GEEK_CRAWLER_MODE"]?.Trim().ToLowerInvariant() ?? "standard";
        var accelerated = string.Equals(mode, "accelerated", StringComparison.Ordinal);

        var workerCount = ParseMinInt(
            configuration["GEEK_CRAWLER_WORKER_COUNT"],
            defaultValue: 1,
            min: 1);

        var defaultParallelism = accelerated ? 4 : 1;
        var defaultDelay = accelerated ? 3 : 12;

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
            SeedsOnly = ParseBool(configuration["GEEK_CRAWLER_SEEDS_ONLY"]),
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
