namespace GeekAPI.Services.ContentCreatorV2.Publish;

/// <summary>Publish triage — CMS upsert vs export-only (see plan § Publish triage).</summary>
public static class GccV2PublishTypes
{
    private static readonly HashSet<string> CmsTypes =
        new(StringComparer.OrdinalIgnoreCase) { "pillar", "blog", "tool" };

    private static readonly HashSet<string> ExportOnlyTypes =
        new(StringComparer.OrdinalIgnoreCase) { "email", "social", "ads", "image-prompt" };

    public static bool IsCmsPublishType(string? contentType) =>
        CmsTypes.Contains(Normalize(contentType));

    public static bool IsExportOnlyType(string? contentType) =>
        ExportOnlyTypes.Contains(Normalize(contentType));

    public static string Normalize(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "blog" : contentType.Trim().ToLowerInvariant();
}
