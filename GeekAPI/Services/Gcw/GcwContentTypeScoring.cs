namespace GeekAPI.Services.Gcw;

using GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Content-type-aware SEO/GEO thresholds for Content Creator v2 VALIDATE.</summary>
public static class GcwContentTypeScoring
{
    public const string AppendFaqSectionKey = "__append:faq";

    public static bool IsShortForm(string? contentType) =>
        contentType is "email" or "social" or "ads" or "image-prompt";

    public static bool IsCarouselJob(string? contentType) =>
        GccV2ChannelTypes.IsLinkedIn(contentType);

    public static bool ExpectsFaqSection(string? contentType) =>
        GccV2LongFormTypes.ExpectsFaqSection(contentType);

    public static bool IsLongForm(string? contentType) =>
        GccV2LongFormTypes.IsLongForm(contentType);

    public static (int MinWords, int MinSections, bool ApplyLengthChecks) GetSeoLengthRules(string? contentType)
    {
        if (IsShortForm(contentType))
            return (0, 0, false);

        if (IsCarouselJob(contentType))
            return (400, 6, true);

        return GccV2LongFormTypes.GetSeoLengthRules(contentType);
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
