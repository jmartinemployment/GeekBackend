using System.Text;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

internal enum ResearchBriefPhase
{
    ArticleMetadata,
    ArticleBody,
    ArticleSection,
    ArticleFaq,
    Review,
    BlogSection,
    ToolBody
}

/// <summary>
/// Builds phase-specific research briefs so each LLM call only receives the context it needs.
/// Keyword SERP files stay tight (intent/ranking patterns); Wikipedia/.edu/.gov are body-only sources to quote.
/// </summary>
internal static class ResearchBriefBuilder
{
    private static readonly KeywordSourceCategory[] AuthoritativeCategories =
    [
        KeywordSourceCategory.Wikipedia,
        KeywordSourceCategory.EduDomain,
        KeywordSourceCategory.GovDomain
    ];

    /// <summary>Brief-only variant, without the trailing per-call instruction block — lets callers
    /// that loop multiple calls per generation (pillar sections, Tools platforms) place this
    /// byte-identical content as a stable prefix ahead of whatever varies per call, so OpenAI's
    /// automatic prompt caching can actually discount it. See the 3-arg overload for callers that
    /// only make one call and don't need the prefix to stay stable.</summary>
    public static string Build(ProjectGenerationContext context, ResearchBriefPhase phase) =>
        BuildCore(context, phase).ToString();

    public static string Build(ProjectGenerationContext context, ResearchBriefPhase phase, string instructions)
    {
        var sb = BuildCore(context, phase);
        sb.AppendLine();
        sb.AppendLine("=== INSTRUCTIONS ===");
        sb.AppendLine(instructions);
        return sb.ToString();
    }

    private static StringBuilder BuildCore(ProjectGenerationContext context, ResearchBriefPhase phase)
    {
        var sb = new StringBuilder();

        switch (phase)
        {
            case ResearchBriefPhase.ArticleMetadata:
                AppendCompactSiteContext(sb, context, includeJsonLd: false);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 6, maxParagraphsPerFile: 0);
                AppendCompetitorGapsBrief(sb, context);
                AppendPaaBrief(sb, context, forFaqSectionOnly: true);
                AppendKnownToolsBrief(sb, context);
                break;

            case ResearchBriefPhase.ArticleBody:
                AppendCompactSiteContext(sb, context, includeJsonLd: true);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 4, maxParagraphsPerFile: 2);
                AppendAuthoritativeSourcesBrief(sb, context);
                AppendCompetitorGapsBrief(sb, context);
                AppendKnownToolsBrief(sb, context);
                // PAA listed only in the body prompt FAQ block — omit here to avoid FAQ-shaped articles.
                break;

            case ResearchBriefPhase.ArticleSection:
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 3, maxParagraphsPerFile: 1);
                AppendAuthoritativeSourcesBrief(sb, context);
                AppendKnownToolsBrief(sb, context);
                break;

            case ResearchBriefPhase.ArticleFaq:
                AppendAuthoritativeSourcesBrief(sb, context);
                break;

            case ResearchBriefPhase.Review:
                // Kept deliberately tight: Review already carries the full rendered body HTML of
                // the content under review, so this brief only needs to supply enough to spot-check
                // rubric point 4 (invented facts) — not a second research pass.
                AppendAuthoritativeSourcesBrief(sb, context, maxSources: 2, maxHeadingsPerFile: 2, maxParagraphsPerFile: 2);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 2, maxParagraphsPerFile: 0);
                break;

            case ResearchBriefPhase.BlogSection:
                AppendCompactSiteContext(sb, context, includeJsonLd: false);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 3, maxParagraphsPerFile: 1);
                AppendAuthoritativeSourcesBrief(sb, context, maxSources: 2, maxHeadingsPerFile: 2, maxParagraphsPerFile: 2);
                AppendCompetitorGapsBrief(sb, context);
                AppendKnownToolsBrief(sb, context);
                break;

            case ResearchBriefPhase.ToolBody:
                AppendAuthoritativeSourcesBrief(sb, context, maxSources: 1, maxHeadingsPerFile: 2, maxParagraphsPerFile: 2);
                AppendKnownToolsBrief(sb, context);
                break;
        }

        return sb;
    }

    /// <summary>
    /// Render the assignment as an indented outline.
    /// </summary>
    /// <remarks>
    /// The predecessor flattened heading depth and anchors into syntax that had to be re-parsed
    /// to be used anywhere else. Indentation carries the same structure here, and each link keeps
    /// its href so the model can name a tool and cite where it came from.
    /// </remarks>
    private static void AppendAssignment(StringBuilder sb, HierarchyAssignment node, int depth)
    {
        var pad = new string(' ', depth * 2);
        if (!string.IsNullOrWhiteSpace(node.Heading))
        {
            var level = node.Level > 0 ? $" (h{node.Level})" : "";
            sb.AppendLine($"{pad}- {node.Heading}{level}");
        }

        foreach (var paragraph in node.Paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph)) continue;
            sb.AppendLine($"{pad}  {paragraph.Trim()}");
        }

        foreach (var link in node.Links)
        {
            if (string.IsNullOrWhiteSpace(link.Name)) continue;
            sb.AppendLine(string.IsNullOrWhiteSpace(link.Href)
                ? $"{pad}  * {link.Name}"
                : $"{pad}  * {link.Name} -> {link.Href}");
        }

        foreach (var child in node.Children)
            AppendAssignment(sb, child, depth + 1);
    }

    private static void AppendCompactSiteContext(StringBuilder sb, ProjectGenerationContext context, bool includeJsonLd)
    {
        sb.AppendLine($"=== PROJECT SITE: {context.SiteName} ({context.ProjectUrl}) ===");
        sb.AppendLine(GeekAPI.Services.Workflow.Services.BrandTones.ForWebpages());
        // Tone/focus omitted this phase — do not invent from crawl leftovers.

        var children = context.HierarchyChildHeadings ?? Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(context.HierarchyPath) || children.Count > 0
            || context.HierarchyAssignment is not null)
        {
            sb.AppendLine("=== SITE ANALYZER ASSIGNMENT ===");
            if (!string.IsNullOrWhiteSpace(context.HierarchyPath))
                sb.AppendLine($"Matched heading path: {context.HierarchyPath}");
            if (!string.IsNullOrWhiteSpace(context.HierarchySourcePageUrl))
                sb.AppendLine($"Source page: {context.HierarchySourcePageUrl}");
            if (context.HierarchyAssignment is not null)
            {
                sb.AppendLine("Write about this matched heading and the outline below (child headings and their links are the assignment, not optional flavor):");
                AppendAssignment(sb, context.HierarchyAssignment, depth: 0);
            }
            if (children.Count > 0)
            {
                sb.AppendLine("Child topics from the analyzed site:");
                foreach (var child in children)
                    sb.AppendLine($"- {child}");
            }
        }

        if (context.CrawledHeadings.Count > 0)
        {
            sb.AppendLine("Key site headings:");
            foreach (var h in context.CrawledHeadings.Take(12)) sb.AppendLine($"- {h}");
        }

        if (context.CrawledParagraphs.Count > 0)
        {
            sb.AppendLine("Representative site copy:");
            var taken = SelectSiteCopyParagraphs(context.CrawledParagraphs);
            // #region agent log
            {
                var total = context.CrawledParagraphs.Count;
                var mustInAll = context.CrawledParagraphs.Any(p =>
                    p.Contains("Partner tools for this use case", StringComparison.Ordinal)
                    || p.Contains("MUST MENTION partner tools", StringComparison.Ordinal));
                var mustInTaken = taken.Any(p =>
                    p.Contains("Partner tools for this use case", StringComparison.Ordinal)
                    || p.Contains("MUST MENTION partner tools", StringComparison.Ordinal));
                var researchInAll = context.CrawledParagraphs.Any(p =>
                    p.Contains("PARTNER PAGE EXCERPTS", StringComparison.Ordinal)
                    || p.Contains("PARTNER PAGE RESEARCH", StringComparison.Ordinal));
                var researchInTaken = taken.Any(p =>
                    p.Contains("PARTNER PAGE EXCERPTS", StringComparison.Ordinal)
                    || p.Contains("PARTNER PAGE RESEARCH", StringComparison.Ordinal));
                GeekAPI.Diagnostics.AgentDebugLog.Write(
                    "A",
                    "ResearchBriefBuilder.AppendSiteContext",
                    "CrawledParagraphs selection applied",
                    new
                    {
                        total,
                        takenCount = taken.Count,
                        mustInAll,
                        mustInTaken,
                        researchInAll,
                        researchInTaken,
                        truncatedAway = mustInAll && !mustInTaken,
                    });
            }
            // #endregion
            foreach (var p in taken) sb.AppendLine($"- {p}");
        }

        if (includeJsonLd && !string.IsNullOrWhiteSpace(context.JsonLdStructuredSummary))
        {
            sb.AppendLine();
            sb.AppendLine(context.JsonLdStructuredSummary);
        }
    }

    /// <summary>
    /// Soft site-copy cap of 5, but never drop partner-tool / excerpt blocks
    /// (v2 packs those into CrawledParagraphs after brand fluff).
    /// </summary>
    internal static List<string> SelectSiteCopyParagraphs(IReadOnlyList<string> paragraphs)
    {
        if (paragraphs.Count == 0) return [];

        var mustIdx = -1;
        var researchIdx = -1;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (mustIdx < 0 && IsPartnerToolsBlock(paragraphs[i]))
                mustIdx = i;
            if (researchIdx < 0 && IsPartnerExcerptBlock(paragraphs[i]))
                researchIdx = i;
        }

        var start = mustIdx >= 0 ? mustIdx : researchIdx;
        if (start < 0)
            return paragraphs.Take(5).ToList();

        // Keep brand fluff preview (up to 2) then partner block with a generous line budget.
        var taken = new List<string>();
        foreach (var p in paragraphs.Take(Math.Min(2, start)))
            taken.Add(p);

        const int maxPartnerLines = 80;
        for (var i = start; i < paragraphs.Count && taken.Count < 2 + maxPartnerLines; i++)
            taken.Add(paragraphs[i]);

        return taken;
    }

    private static bool IsPartnerToolsBlock(string p) =>
        p.Contains("Partner tools for this use case", StringComparison.Ordinal)
        || p.Contains("MUST MENTION partner tools", StringComparison.Ordinal);

    private static bool IsPartnerExcerptBlock(string p) =>
        p.Contains("PARTNER PAGE EXCERPTS", StringComparison.Ordinal)
        || p.Contains("PARTNER PAGE RESEARCH", StringComparison.Ordinal);

    private static void AppendKeywordSerpBrief(
        StringBuilder sb,
        ProjectGenerationContext context,
        int maxHeadingsPerFile,
        int maxParagraphsPerFile)
    {
        var titles = SplitMultiline(context.SerpTitles);
        var urls = SplitMultiline(context.SerpUrls);
        var related = SplitMultiline(context.SerpRelatedSearches);

        if (titles.Count > 0 || urls.Count > 0 || related.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== KEYWORD SERP (curated organic index — search intent / competitor titles) ===");
            var n = Math.Max(titles.Count, urls.Count);
            for (var i = 0; i < n; i++)
            {
                var title = i < titles.Count ? titles[i] : null;
                var url = i < urls.Count ? urls[i] : null;
                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                    sb.AppendLine($"- {title} ({url})");
                else if (!string.IsNullOrWhiteSpace(title))
                    sb.AppendLine($"- {title}");
                else if (!string.IsNullOrWhiteSpace(url))
                    sb.AppendLine($"- {url}");
            }

            if (related.Count > 0)
            {
                sb.AppendLine("Related searches:");
                foreach (var r in related.Take(20)) sb.AppendLine($"- {r}");
            }

            // Curated index replaces chrome-heading scrape from KeywordResult uploads.
            return;
        }

        // No curated SERP index — do not inject generic h1–h3 chrome from KeywordResult files.
        _ = maxHeadingsPerFile;
        _ = maxParagraphsPerFile;
    }

    private static List<string> SplitMultiline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static void AppendAuthoritativeSourcesBrief(
        StringBuilder sb,
        ProjectGenerationContext context,
        int maxSources = int.MaxValue,
        int maxHeadingsPerFile = 6,
        int maxParagraphsPerFile = 8)
    {
        var sources = context.KeywordSources
            .Where(s => AuthoritativeCategories.Contains(s.Category))
            .Take(maxSources)
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== AUTHORITATIVE SOURCES (quote, paraphrase, and attribute facts to these) ===");
        foreach (var source in sources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            foreach (var h in source.Headings.Take(maxHeadingsPerFile)) sb.AppendLine($"- {h}");
            foreach (var p in source.Paragraphs.Take(maxParagraphsPerFile)) sb.AppendLine($"- {p}");
        }
    }

    private static void AppendCompetitorGapsBrief(StringBuilder sb, ProjectGenerationContext context)
    {
        var sources = context.KeywordSources
            .Where(s => s.Category is KeywordSourceCategory.CompetitorCrawl or KeywordSourceCategory.Local)
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== COMPETITOR / LOCAL SERP (headings only — identify gaps to cover better) ===");
        foreach (var source in sources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            foreach (var h in source.Headings.Take(8)) sb.AppendLine($"- {h}");
        }
    }

    private static void AppendPaaBrief(StringBuilder sb, ProjectGenerationContext context, bool forFaqSectionOnly)
    {
        if (context.PeopleAlsoAskQuestions.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        if (forFaqSectionOnly)
        {
            sb.AppendLine("=== PEOPLE ALSO ASK (dedicated FAQ section at end — H2 \"People Also Ask\", each question as H3) ===");
        }
        else
        {
            sb.AppendLine("=== PEOPLE ALSO ASK (answer naturally in the body) ===");
        }

        foreach (var q in context.PeopleAlsoAskQuestions.Take(15)) sb.AppendLine($"- {q}");
    }

    /// <summary>
    /// Grounds pillar/blog/tool prose in crawl tool names fetched at generate time.
    /// Instructs named, substantive discussion in running paragraphs — not a roll-call and not links alone.
    ///
    /// <para>
    /// A named tool also links to its own page. Jeff, 2026-09-26: "Tool mentions should be Next js
    /// &lt;Links&gt; to /tools". That is a statement about the href, not about the markup: the site
    /// renders a body link as a Next <c>Link</c> when the href is a relative path and as a plain
    /// external anchor when it starts <c>http</c> (<c>article-body.tsx</c>, via
    /// <c>external: /^https?:/i.test(href)</c> in <c>article-sections.ts</c>). So the public path is
    /// what makes the mention a <c>Link</c>, and a vendor URL is what stops it being one.
    /// </para>
    ///
    /// <para>
    /// Linking was optional here and the drafts took the option: tools were named and none of them
    /// went anywhere. It is now the first substantive mention in each section, capped there so prose
    /// does not turn into a row of links.
    /// </para>
    /// </summary>
    private static void AppendKnownToolsBrief(StringBuilder sb, ProjectGenerationContext context)
    {
        var tools = context.KnownCrawlTools ?? Array.Empty<KnownCrawlTool>();
        if (tools.Count == 0)
        {
            return;
        }

        var dept = string.IsNullOrWhiteSpace(context.Department) ? "marketing" : context.Department.Trim();

        sb.AppendLine();
        sb.AppendLine("=== KNOWN TOOLS (grounding — weave into this content) ===");
        sb.AppendLine(
            "Name these tools in running paragraphs wherever each is relevant to THIS section or page. " +
            "Discuss them substantively — what they do for this use case, not a one-word mention. " +
            "Do not produce a roll-call list, and do not write a Tools heading or catalog. " +
            "Recurring mentions are fine when they add something; first-mention-only is not enough. " +
            "A link is never a substitute for discussing the tool.");
        sb.AppendLine(
            "Every tool below has a public path on this site, and a named tool links to it: set Run.href to that " +
            "exact path on the first substantive body mention of each tool in each section. Later mentions of the " +
            "same tool in that same section stay plain text — one link per tool per section, so the prose does not " +
            "become a row of links. Headings are never linked.");
        sb.AppendLine(
            "The path is relative and starts with a slash, exactly as written below. Do not link a tool to the " +
            "vendor's own website, to the crawl source page, or to any absolute URL, and never fabricate a path: " +
            "an off-site href is not the link being asked for here.");
        sb.AppendLine("Tools from the crawl:");
        foreach (var tool in tools)
        {
            var slug = SlugHelper.Slugify(tool.Name);
            var publicPath = $"/tools/{dept}/{slug}";
            if (!string.IsNullOrWhiteSpace(tool.Href))
            {
                sb.AppendLine($"- {tool.Name} — public path: {publicPath} (crawl source, do not send the reader there: {tool.Href})");
            }
            else
            {
                sb.AppendLine($"- {tool.Name} — public path: {publicPath}");
            }
        }
    }

    private static string FormatSourceLabel(KeywordSourceSummary source) =>
        !string.IsNullOrWhiteSpace(source.Title) ? source.Title! : source.SourceLabel;
}
