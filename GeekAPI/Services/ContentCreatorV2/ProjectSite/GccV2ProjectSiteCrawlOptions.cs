namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public sealed class GccV2ProjectSiteCrawlOptions
{
    public const int BatchSaveSize = 10;
    public const int DefaultMaxPages = 50;
    public const int DefaultParallelism = 2;
    public const int DefaultHostDelayMs = 1200;

    public int MaxPages { get; init; } = DefaultMaxPages;
    public int Parallelism { get; init; } = DefaultParallelism;
    public int HostDelayMs { get; init; } = DefaultHostDelayMs;

    public static GccV2ProjectSiteCrawlOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            MaxPages = ParseBoundedInt(configuration["GCC_V2_PROJECT_SITE_MAX_PAGES"], DefaultMaxPages, 1, 200),
            Parallelism = ParseBoundedInt(configuration["GCC_V2_PROJECT_SITE_PARALLELISM"], DefaultParallelism, 1, 8),
            HostDelayMs = ParseBoundedInt(configuration["GCC_V2_PROJECT_SITE_HOST_DELAY_MS"], DefaultHostDelayMs, 0, 30_000),
        };

    private static int ParseBoundedInt(string? raw, int defaultValue, int min, int max)
    {
        if (!int.TryParse(raw, out var value))
            return defaultValue;
        return Math.Clamp(value, min, max);
    }
}
