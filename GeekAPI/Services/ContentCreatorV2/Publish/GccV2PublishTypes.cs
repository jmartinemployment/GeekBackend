using GeekAPI.Services.ContentCreatorV2.ContentTypes;

using GeekAPI.Services.ContentCreatorV2.ContentTypes;

/// <summary>Publish triage — CMS upsert vs export-only (see plan § Publish triage).</summary>
public static class GccV2PublishTypes
{
    private static readonly HashSet<string> ExportOnlyTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "email", "social", "ads", "image-prompt", "whitepaper", GccV2ChannelTypes.LinkedInCarousel,
        };

    public static bool IsCmsPublishType(string? contentType) =>
        GccV2LongFormTypes.IsCmsPublishable(contentType);

    public static bool IsExportOnlyType(string? contentType) =>
        ExportOnlyTypes.Contains(Normalize(contentType));

    public static string Normalize(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "blog" : contentType.Trim().ToLowerInvariant();
}
