namespace GeekAPI.Services.Gcw;

/// <summary>
/// Default channel mix for pillar → short-form / ad packs (Copy.ai-class).
/// </summary>
public static class GcwRepurposeCatalog
{
    public sealed record ChannelSpec(
        string Slug,
        string Name,
        int DefaultCount,
        string Guidance);

    public static readonly IReadOnlyList<ChannelSpec> Channels =
    [
        new(
            "linkedin",
            "LinkedIn",
            3,
            "Professional insight posts: hook in first line, 120–220 words, soft CTA, 3–5 hashtags."),
        new(
            "x",
            "X / Twitter",
            3,
            "Punchy posts under 260 characters. One idea each. Optional 1–2 hashtags."),
        new(
            "instagram",
            "Instagram",
            2,
            "Caption-style: visual opener, short body, CTA, 5–8 hashtags."),
        new(
            "meta_ad",
            "Meta ad",
            2,
            "Primary text (~125 words) + short headline + CTA for feed ads."),
        new(
            "google_ad",
            "Google ad",
            1,
            "RSA-style: headline ≤30 chars, description ≤90 chars, clear CTA."),
        new(
            "email",
            "Email snippet",
            1,
            "Subject-line style headline + 80–120 word body + CTA for nurture."),
    ];

    public static string BuildChannelBrief(IEnumerable<string>? channelFilter)
    {
        var filter = channelFilter?
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = Channels
            .Where(c => filter is null || filter.Count == 0 || filter.Contains(c.Slug))
            .ToList();

        if (selected.Count == 0)
            selected = Channels.ToList();

        var lines = selected.Select(c =>
            $"- {c.Slug} × {c.DefaultCount}: {c.Name}. {c.Guidance}");

        return
            "Produce exactly the counts below (no more, no fewer). Each item is one variant.\n" +
            string.Join("\n", lines);
    }

    public static int ExpectedVariantCount(IEnumerable<string>? channelFilter)
    {
        var filter = channelFilter?
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Channels
            .Where(c => filter is null || filter.Count == 0 || filter.Contains(c.Slug))
            .Sum(c => c.DefaultCount);
    }
}
