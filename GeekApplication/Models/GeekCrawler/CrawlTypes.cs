namespace GeekApplication.Models.GeekCrawler;

/// <summary>Why a crawl was started — stored as lowercase kebab-case in the database.</summary>
public static class CrawlTypes
{
    public const string Competitors = "competitors";
    public const string Partner = "partner";
    public const string Local = "local";

    private static readonly HashSet<string> Valid = new(StringComparer.Ordinal)
    {
        Competitors,
        Partner,
        Local,
    };

    public static bool IsValid(string? crawlType) =>
        !string.IsNullOrWhiteSpace(crawlType) && Valid.Contains(crawlType.Trim());
}
