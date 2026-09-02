namespace GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Canonical long-form content types for gcc-v2 PLAN / WRITE / VALIDATE / export.</summary>
public static class GccV2LongFormTypes
{
    public const string Pillar = "pillar";
    public const string Blog = "blog";
    public const string Tool = "tool";
    public const string Comparison = "comparison";
    public const string CaseStudy = "case-study";
    public const string Guide = "guide";
    public const string Alternatives = "alternatives";
    public const string TechArticle = "tech-article";
    public const string Listicle = "listicle";
    public const string Service = "service";
    public const string Local = "local";
    public const string Whitepaper = "whitepaper";

    private static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Pillar, Blog, Tool, Comparison, CaseStudy, Guide, Alternatives,
        TechArticle, Listicle, Service, Local, Whitepaper,
    };

    private static readonly HashSet<string> ArticleLike = new(StringComparer.OrdinalIgnoreCase)
    {
        Pillar, Blog, Comparison, CaseStudy, Guide, Alternatives,
        TechArticle, Listicle, Service, Local, Whitepaper,
    };

    private static readonly HashSet<string> ExpectsFaq = new(StringComparer.OrdinalIgnoreCase)
    {
        Pillar, Blog, Comparison, Guide, Alternatives, TechArticle, Listicle, Local,
    };

    private static readonly HashSet<string> HeroAndSectionImagePrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        Pillar, Blog, Comparison, CaseStudy, Guide, Alternatives,
        TechArticle, Listicle, Service, Local, Whitepaper,
    };

    private static readonly HashSet<string> CmsPublishable = new(StringComparer.OrdinalIgnoreCase)
    {
        Pillar, Blog, Tool, Comparison, CaseStudy, Guide, Alternatives,
        TechArticle, Listicle, Service, Local,
    };

    public static bool IsLongForm(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return All.Contains(contentType.Trim());
    }

    public static bool IsArticleLike(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && ArticleLike.Contains(contentType.Trim());

    public static bool ExpectsFaqSection(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && ExpectsFaq.Contains(contentType.Trim());

    public static bool UsesHeroAndSectionImagePrompts(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && HeroAndSectionImagePrompts.Contains(contentType.Trim());

    public static bool IsCmsPublishable(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && CmsPublishable.Contains(contentType.Trim());

    public static string Normalize(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? Blog : contentType.Trim().ToLowerInvariant();

    public static (int MinWords, int MinSections, bool ApplyLengthChecks) GetSeoLengthRules(string? contentType)
    {
        return Normalize(contentType) switch
        {
            Pillar => (3000, 3, true),
            Blog => (1800, 3, true),
            Tool => (1500, 2, true),
            Comparison => (2200, 4, true),
            CaseStudy => (1800, 4, true),
            Guide => (2500, 4, true),
            Alternatives => (2000, 3, true),
            TechArticle => (2800, 3, true),
            Listicle => (2000, 5, true),
            Service => (1200, 3, true),
            Local => (1500, 3, true),
            Whitepaper => (4000, 5, true),
            _ => (800, 3, true),
        };
    }

    public static string ExportFolder(string? contentType) => Normalize(contentType) switch
    {
        Pillar => "use-cases",
        Blog => "blog",
        Tool => "tools",
        Comparison => "comparison",
        CaseStudy => "case-studies",
        Guide => "guides",
        Alternatives => "alternatives",
        TechArticle => "tech-articles",
        Listicle => "listicles",
        Service => "services",
        Local => "local",
        Whitepaper => "whitepapers",
        _ => "articles",
    };

    public static IReadOnlyList<string> AllTypes => All.ToList();
}
