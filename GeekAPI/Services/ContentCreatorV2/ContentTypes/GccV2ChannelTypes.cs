namespace GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Export-only channel content types (not long-form web pages).</summary>
public static class GccV2ChannelTypes
{
    public const string LinkedInCarousel = "linkedin-carousel";

    public static bool IsLinkedInCarousel(string? contentType) =>
        string.Equals(contentType?.Trim(), LinkedInCarousel, StringComparison.OrdinalIgnoreCase);
}
