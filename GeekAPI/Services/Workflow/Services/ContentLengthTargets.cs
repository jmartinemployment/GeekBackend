namespace GeekAPI.Services.Workflow.Services;

/// <summary>Recommended word-count ranges and editorial definitions by content type.</summary>
public static class ContentLengthTargets
{
    // The lede — the opening hook, on every long-form type.
    //
    // It had no word target at all while every body section had one, so "2-3 paragraphs" was the
    // only size signal and three one-sentence paragraphs satisfied it literally. Jeff, 2026-09-23,
    // twice: "Lede paragraph way to short" and then "Lede paragraph still ridiculously short!
    // Should be 3 x that length it is LAME."
    public const int LedeMinWords = 250;
    public const int LedeTargetMaxWords = 400;

    /// <summary>A paragraph below this has not made a point; it is a sentence with space after it.</summary>
    public const int LedeParagraphMinWords = 70;

    public static string LedeRangeLabel => $"{LedeMinWords}-{LedeTargetMaxWords}";

    // Pillar Pages — exhaustive macro-level hubs linking to cluster articles.
    public const int PillarMinWords = 3_000;
    public const int PillarTargetMinWords = 3_500;
    public const int PillarTargetMaxWords = 5_000;
    public const int PillarSectionMinWords = 500;
    public const int PillarSectionTargetMaxWords = 700;
    // The tools index on a keyword overview page (v2). Named for the pillar's Tools section until
    // 2026-09-26, when that section was removed -- a pillar carries no Tools H2, so a budget for one
    // reads as evidence that it does.
    public const int ToolsIndexSectionMinWords = 700;
    public const int ToolsIndexSectionTargetMaxWords = 900;

    public const string PillarEditorialDefinition =
        "Pillar pages are exhaustive, macro-level entry points for massive topics. They host multiple subsections " +
        "and link out to smaller cluster articles. Quality means comprehensive coverage — not padding.";

    // Deep-Dive Blog Posts — companion articles aimed at outranking competitors.
    // Raised from 1,800-2,500 on 2026-09-23. Blog landed 300-400 words under target run after run,
    // so the stated range moves up to where the output actually needs to be (Jeff: "Blog is
    // consistently 300-400 words short of target, so increase target range to 2000 - 2700 words").
    // The per-section budget moves with it -- a total nothing supports section by section is the
    // number the model already ignores.
    public const int BlogMinWords = 2_000;
    public const int BlogTargetMinWords = 2_000;
    public const int BlogTargetMaxWords = 2_700;
    public const int BlogSectionMinWords = 450;
    public const int BlogSectionTargetMaxWords = 600;
    public const int BlogSectionCountMin = 5;
    public const int BlogSectionCountTarget = 6;

    public const string BlogEditorialDefinition =
        "Deep-dive blog posts are the sweet spot for standard articles trying to outrank competitors on search engines. " +
        "Each section must add real depth: context, examples, data, and actionable insight — not surface summaries.";

    // Standard Listicles & Guides — actionable step-by-step tutorials (future content type).
    public const int ListicleGuideMinWords = 1_200;
    public const int ListicleGuideMaxWords = 1_800;

    public const string ListicleGuideEditorialDefinition =
        "Standard listicles and guides are actionable, step-by-step tutorials that require substantial context, " +
        "data, and layout formatting. Used when the reader needs a practical walkthrough, not a macro overview.";

    // Tool pages — the revenue-critical content type (Jeff, 2026-09-22): partner-grounded, and
    // equal to Pillar in word count if not longer, never shorter. Matches PillarMinWords/
    // PillarTargetMinWords/PillarTargetMaxWords exactly rather than merely approaching them.
    public const int ToolMinWords = PillarMinWords;
    public const int ToolTargetMinWords = PillarTargetMinWords;
    public const int ToolTargetMaxWords = PillarTargetMaxWords;
    public const int ToolHardMaxWords = PillarTargetMaxWords;

    public const string ToolEditorialDefinition =
        "Tool pages are comprehensive, partner-grounded guides for a single platform — deep " +
        "implementation context, capabilities, evaluation criteria, and guidance on when to use it. " +
        "Equal in depth to a Pillar page, never a thinner treatment.";

    public static string ToolRangeLabel => $"{ToolMinWords:N0}–{ToolTargetMaxWords:N0}";

    // News & quick updates (reference for future content types).
    public const int NewsMinWords = 400;
    public const int NewsMaxWords = 800;

    public const string NewsEditorialDefinition =
        "News and quick updates are best for press releases or short announcements — timely, concise, and direct.";

    public static string PillarRangeLabel => $"{PillarMinWords:N0}–{PillarTargetMaxWords:N0}+";
    public static string BlogRangeLabel => $"{BlogTargetMinWords:N0}–{BlogTargetMaxWords:N0}";
    public static string ListicleGuideRangeLabel => $"{ListicleGuideMinWords:N0}–{ListicleGuideMaxWords:N0}";

    // Email — Cold Outreach / Sales (implemented).
    public const int EmailColdOutreachMinWords = 50;
    public const int EmailColdOutreachMaxWords = 125;

    public const string EmailColdOutreachEditorialDefinition =
        "Cold outreach and sales emails aim for high response rates with a single, clear call-to-action.";

    // Email stubs (constants only — no generation yet).
    public const int EmailNewsletterMinWords = 200;
    public const int EmailNewsletterMaxWords = 400;

    public const string EmailNewsletterEditorialDefinition =
        "Curated newsletters summarize external links and drive traffic back to the website.";

    public const int EmailStoryNurtureMinWords = 500;
    public const int EmailStoryNurtureMaxWords = 1_000;

    public const string EmailStoryNurtureEditorialDefinition =
        "Story-based nurture emails build deep trust and treat email like an exclusive blog post.";

    public const int EmailTransactionalMinWords = 1;
    public const int EmailTransactionalMaxWords = 49;

    public const string EmailTransactionalEditorialDefinition =
        "Transactional emails deliver critical data; highly functional with zero fluff.";

    public static string EmailColdOutreachRangeLabel =>
        $"{EmailColdOutreachMinWords}–{EmailColdOutreachMaxWords}";
}

