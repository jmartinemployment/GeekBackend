namespace GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Export-only channel content types (not long-form web pages).</summary>
public static class GccV2ChannelTypes
{
    public const string LinkedInDocument = "linkedin-document";

    public static bool IsLinkedInDocument(string? contentType)
    {
        var normalized = contentType?.Trim();
        return string.Equals(normalized, LinkedInDocument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "linkedin-carousel", StringComparison.OrdinalIgnoreCase);
    }
}
