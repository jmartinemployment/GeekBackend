namespace GeekAPI.Services.ContentCreatorV2.Transforms;

/// <summary>Generate job types that may be Re-Purposed into channel packs. Image-prompt sidecars are excluded.</summary>
public static class GccV2RepurposeSourceTypes
{
    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "pillar",
        "blog",
        "tool",
        "email",
        "social",
        "ads",
    };

    public static bool IsAllowed(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType)
        && Allowed.Contains(contentType.Trim());

    public static string GuidanceFor(string contentType) =>
        (contentType ?? "").Trim().ToLowerInvariant() switch
        {
            "pillar" => "The source is a long-form pillar / use-case article.",
            "blog" => "The source is a blog post draft.",
            "tool" => "The source is a partner tool overview page.",
            "email" => "The source is an email outreach draft.",
            "social" => "The source is a social media post draft.",
            "ads" => "The source is paid advertising copy.",
            _ => "The source is marketing content.",
        };
}
