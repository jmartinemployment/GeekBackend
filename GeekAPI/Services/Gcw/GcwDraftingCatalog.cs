namespace GeekAPI.Services.Gcw;

/// <summary>
/// Static Horizon B draft templates and tone presets for GCW generate/revise.
/// Kept in GeekAPI (not CWV4 DB) so GCW can ship without seeding CWV4 templates first.
/// </summary>
public static class GcwDraftingCatalog
{
    public sealed record DraftTemplate(
        string Slug,
        string Name,
        string Description,
        string Category,
        string Guidance);

    public sealed record TonePreset(
        string Slug,
        string Name,
        string Description,
        string Guidance);

    public static readonly IReadOnlyList<DraftTemplate> Templates =
    [
        new(
            "blog-pillar",
            "Blog pillar",
            "Long-form educational article with clear sections and CTA.",
            "blog",
            "Format as a pillar blog post: strong lede, 4–6 H2 sections, practical examples, closing CTA."),
        new(
            "landing-page",
            "Landing page",
            "Conversion-focused page: problem, proof, offer, CTA.",
            "web",
            "Format as landing-page copy: problem hook, benefit sections, social proof cues, single primary CTA. Keep sections scannable."),
        new(
            "product-description",
            "Product description",
            "Feature → benefit product copy for a page or catalog.",
            "product",
            "Format as product description: opening value prop, feature→benefit bullets in sections, objection handling, purchase CTA."),
        new(
            "case-study",
            "Case study",
            "Challenge → approach → results narrative.",
            "proof",
            "Format as a case study: challenge, approach, implementation, measurable results, lessons, CTA to talk."),
        new(
            "email-nurture",
            "Email nurture",
            "Short nurture email with one clear ask.",
            "email",
            "Format as an email: punchy lede as preview/open, 2–3 short body sections, one clear CTA. Prefer brevity over length."),
    ];

    public static readonly IReadOnlyList<TonePreset> Tones =
    [
        new(
            "professional",
            "Professional",
            "Clear, credible B2B voice.",
            "Tone: professional and credible. Prefer plain language over hype. Avoid slang."),
        new(
            "punchy",
            "Punchy",
            "Short sentences, energetic, decisive.",
            "Tone: punchy and energetic. Short sentences. Strong verbs. Minimal hedging."),
        new(
            "technical",
            "Technical",
            "Precise, operator-friendly detail.",
            "Tone: technical and precise. Name mechanisms and tradeoffs. Assume a sophisticated reader."),
        new(
            "warm",
            "Warm",
            "Human, empathetic, still on-brand.",
            "Tone: warm and human. Empathize with the reader's situation without becoming casual or fluffy."),
        new(
            "formal",
            "Formal",
            "Conservative enterprise register.",
            "Tone: formal and reserved. Prefer measured claims and complete sentences. No colloquialisms."),
    ];

    public static DraftTemplate? FindTemplate(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? null
            : Templates.FirstOrDefault(t => t.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));

    public static TonePreset? FindTone(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? null
            : Tones.FirstOrDefault(t => t.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string BuildPromptSuffix(string? templateSlug, string? toneSlug)
    {
        var parts = new List<string>();
        var template = FindTemplate(templateSlug);
        var tone = FindTone(toneSlug);
        if (template is not null)
            parts.Add($"Template ({template.Name}): {template.Guidance}");
        if (tone is not null)
            parts.Add(tone.Guidance);
        return parts.Count == 0 ? "" : "Drafting controls:\n- " + string.Join("\n- ", parts);
    }
}
