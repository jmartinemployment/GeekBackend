namespace GeekAPI.Services.Gcw;

/// <summary>
/// Default channel mix for Re-Purpose packs — same output shape whether the source draft is
/// pillar, blog, tool, email, social, or ads (Copy.ai-class).
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
        new(
            "blog",
            "Blog pack",
            1,
            "Short blog-style piece: compelling title, 3–5 paragraph body that preserves the source story arc, soft CTA. Not a listicle."),
        new(
            "image_prompt",
            "Image prompt",
            1,
            "Ready-to-paste image-generation prompts (Midjourney/Flux style): subject, setting, lighting, style, aspect. No marketing fluff. Not used by v2 Re-Purpose — image prompts spawn as jobs per Content Creator §3.1."),
    ];

    public static string BuildChannelBrief(
        IEnumerable<string>? channelFilter,
        IReadOnlyDictionary<string, int>? countOverrides = null)
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
        {
            var count = countOverrides is not null
                && countOverrides.TryGetValue(c.Slug, out var overridden)
                && overridden > 0
                ? overridden
                : c.DefaultCount;
            return $"- {c.Slug} × {count}: {c.Name}. {c.Guidance}";
        });

        return
            "Produce exactly the counts below (no more, no fewer). Each item is one variant.\n" +
            string.Join("\n", lines);
    }

    public static int ExpectedVariantCount(
        IEnumerable<string>? channelFilter,
        IReadOnlyDictionary<string, int>? countOverrides = null)
    {
        var filter = channelFilter?
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Channels
            .Where(c => filter is null || filter.Count == 0 || filter.Contains(c.Slug))
            .Sum(c =>
            {
                if (countOverrides is not null
                    && countOverrides.TryGetValue(c.Slug, out var overridden)
                    && overridden > 0)
                    return overridden;
                return c.DefaultCount;
            });
    }
}
