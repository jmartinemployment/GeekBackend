namespace GeekAPI.Services.Rag;

/// <summary>Writing intents for <c>POST /api/rag/generate</c> — drives retrieval + prompt shape.</summary>
public static class RagWritingIntents
{
    public const string TechnicalArticle = "Technical Article";
    public const string CaseStudy = "Case Study";
    public const string SocialAd = "Social Ad";
    public const string ShortForm = "Short Form";
    public const string CompetitiveBattlecard = "Competitive Battlecard";
    public const string PitchSlides = "Pitch Slides";
    public const string StrategyTheme = "Strategy Theme";

    public static readonly IReadOnlyList<string> All =
    [
        TechnicalArticle,
        CaseStudy,
        SocialAd,
        ShortForm,
        CompetitiveBattlecard,
        PitchSlides,
        StrategyTheme,
    ];

    public static bool TryNormalize(string? raw, out string intent)
    {
        intent = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        foreach (var known in All)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                intent = known;
                return true;
            }
        }

        return false;
    }

    public static RagRetrievalFamily FamilyOf(string intent) =>
        intent switch
        {
            CompetitiveBattlecard => RagRetrievalFamily.Battlecard,
            SocialAd or ShortForm => RagRetrievalFamily.ShortForm,
            PitchSlides or StrategyTheme => RagRetrievalFamily.Slides,
            _ => RagRetrievalFamily.LongForm,
        };
}

public enum RagRetrievalFamily
{
    LongForm,
    ShortForm,
    Battlecard,
    Slides,
}
