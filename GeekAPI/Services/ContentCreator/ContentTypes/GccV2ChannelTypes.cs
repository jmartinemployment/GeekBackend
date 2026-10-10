namespace GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Export-only PDF content types (not long-form web pages).</summary>
public static class GccV2ChannelTypes
{
    /// <summary>Legacy persisted identifier for the PDF option. The UI label is PDF.</summary>
    public const string LinkedInDocument = "linkedin-document";

    /// <summary>Legacy persisted job identifier for generated PDF slide decks.</summary>
    public const string LinkedInCarousel = "linkedin-carousel";

    /// <summary>True for either legacy PDF identifier.</summary>
    public static bool IsLinkedInDocument(string? contentType)
    {
        var normalized = contentType?.Trim();
        return string.Equals(normalized, LinkedInDocument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, LinkedInCarousel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>PDF types deferred from generate until the source long-form draft is ready.</summary>
    public static bool IsLinkedIn(string? contentType) => IsLinkedInDocument(contentType);
}
