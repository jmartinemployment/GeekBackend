namespace GeekApplication.Models.GeekCrawler;

/// <summary>Why a crawl was started — stored as lowercase kebab-case in the database.</summary>
public static class CrawlTypes
{
    /// <summary>
    /// Partner competitors: rivals of the tools the operator partners with (Jeff, 2026-10-14). Not
    /// rivals of the operator's own business; those are <see cref="ConsultancyCompetitors"/>.
    /// </summary>
    public const string Competitors = "competitors";
    public const string Partner = "partner";
    /// <summary>Geography — local SEO pages. NOT the operator's own site; that is <see cref="ProjectSite"/>.</summary>
    public const string Local = "local";
    /// <summary>The operator's own site — source of the site hierarchy that grounds generation.</summary>
    public const string ProjectSite = "project-site";

    /// <summary>
    /// A rival IT consultancy (Jeff, 2026-10-14). The ones that belong in this list are in three South
    /// Florida counties, Palm Beach (West Palm Beach), Broward and Miami-Dade; no code checks where a
    /// site is. Crawled exactly like <see cref="Partner"/>.
    ///
    /// Not <see cref="Competitors"/>, which is the partners' rivals a Content Creator project
    /// researches. Content Creator resolves runs by an exact type, so a crawl of this type is never
    /// read as a project's competitor evidence.
    /// </summary>
    public const string ConsultancyCompetitors = "consultancy-competitors";

    private static readonly HashSet<string> Valid = new(StringComparer.Ordinal)
    {
        Competitors,
        Partner,
        Local,
        ProjectSite,
        ConsultancyCompetitors,
    };

    /// <summary>
    /// The accepted values, for error text. Derived from the set so adding a type cannot leave a
    /// message listing three of four — which it did until 2026-09-17.
    /// </summary>
    public static string ValidListMessage =>
        "crawlType must be one of: " + string.Join(", ", Valid) + ".";

    public static bool IsValid(string? crawlType) =>
        !string.IsNullOrWhiteSpace(crawlType) && Valid.Contains(crawlType.Trim());
}
