namespace GeekAPI.Services.Gcw;

/// <summary>Content-type-aware SEO/GEO thresholds for Content Creator v2 VALIDATE.</summary>
public static class GcwContentTypeScoring
{
    public const string AppendFaqSectionKey = "__append:faq";

    public static bool IsShortForm(string? contentType) =>
        contentType is "email" or "social" or "ads" or "image-prompt";

    public static bool ExpectsFaqSection(string? contentType) =>
        contentType is "pillar" or "blog";

    public static bool IsLongForm(string? contentType) =>
        contentType is "pillar" or "blog" or "tool";

    public static (int MinWords, int MinSections, bool ApplyLengthChecks) GetSeoLengthRules(string? contentType)
    {
        if (IsShortForm(contentType))
            return (0, 0, false);

        return (contentType ?? "blog").Trim().ToLowerInvariant() switch
        {
            "pillar" => (3000, 3, true),
            "blog" => (1800, 3, true),
            "tool" => (1500, 2, true),
            _ => (800, 3, true),
        };
    }

    public static bool GeoCheckApplies(string checkId, string? contentType)
    {
        if (IsShortForm(contentType))
            return checkId is "entity-clarity";

        if (!ExpectsFaqSection(contentType) && checkId == "faq-or-direct-answers")
            return false;

        return true;
    }
}
