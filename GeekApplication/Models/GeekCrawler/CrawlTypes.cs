namespace GeekApplication.Models.GeekCrawler;

/// <summary>Why a crawl was started — stored as lowercase kebab-case in the database.</summary>
public static class CrawlTypes
{
    public const string Competitors = "competitors";
    public const string Partner = "partner";
    /// <summary>Geography — local SEO pages. NOT the operator's own site; that is <see cref="ProjectSite"/>.</summary>
    public const string Local = "local";
    /// <summary>The operator's own site — source of the site hierarchy that grounds generation.</summary>
    public const string ProjectSite = "project-site";

    private static readonly HashSet<string> Valid = new(StringComparer.Ordinal)
    {
        Competitors,
        Partner,
        Local,
        ProjectSite,
    };

    public static bool IsValid(string? crawlType) =>
        !string.IsNullOrWhiteSpace(crawlType) && Valid.Contains(crawlType.Trim());
}
