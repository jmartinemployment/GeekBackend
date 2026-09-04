namespace GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Export-only channel content types (not long-form web pages).</summary>
public static class GccV2ChannelTypes
{
    /// <summary>UI / brief value for LinkedIn PDF document (Also draft).</summary>
    public const string LinkedInDocument = "linkedin-document";

    /// <summary>Persisted job contentType after spawn (canonical WRITE/export value).</summary>
    public const string LinkedInCarousel = "linkedin-carousel";

    /// <summary>True for either UI alias (<c>linkedin-document</c>) or spawned job type (<c>linkedin-carousel</c>).</summary>
    public static bool IsLinkedInDocument(string? contentType)
    {
        var normalized = contentType?.Trim();
        return string.Equals(normalized, LinkedInDocument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, LinkedInCarousel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>LinkedIn channel types deferred from generate (spawn after long-form is ready).</summary>
    public static bool IsLinkedIn(string? contentType) => IsLinkedInDocument(contentType);
}
