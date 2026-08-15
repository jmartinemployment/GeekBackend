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
                break;
        }

        return sb;
    }

    private static void AppendCompactSiteContext(StringBuilder sb, ProjectGenerationContext context, bool includeJsonLd)
    {
        sb.AppendLine($"=== PROJECT SITE: {context.SiteName} ({context.ProjectUrl}) ===");
        sb.AppendLine(GeekAPI.Services.Workflow.Services.BrandTones.ForWebpages());
        // Tone/focus omitted this phase — do not invent from crawl leftovers.

        var children = context.HierarchyChildHeadings ?? Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(context.HierarchyPath) || children.Count > 0
            || !string.IsNullOrWhiteSpace(context.HierarchyAssignmentMarkdown))
        {
            sb.AppendLine("=== SITE ANALYZER ASSIGNMENT ===");
            if (!string.IsNullOrWhiteSpace(context.HierarchyPath))
                sb.AppendLine($"Matched heading path: {context.HierarchyPath}");
            if (!string.IsNullOrWhiteSpace(context.HierarchySourcePageUrl))
                sb.AppendLine($"Source page: {context.HierarchySourcePageUrl}");
            if (!string.IsNullOrWhiteSpace(context.HierarchyAssignmentMarkdown))
            {
                sb.AppendLine("Write about this matched heading and the markdown below (child headings and lists are the assignment, not optional flavor):");
                sb.AppendLine(context.HierarchyAssignmentMarkdown);
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
            foreach (var p in context.CrawledParagraphs.Take(5)) sb.AppendLine($"- {p}");
        }

        if (includeJsonLd && !string.IsNullOrWhiteSpace(context.JsonLdStructuredSummary))
        {
            sb.AppendLine();
            sb.AppendLine(context.JsonLdStructuredSummary);
        }
    }

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
    /// Grounds pillar/blog prose in Site Analyzer tool names + uploaded tool research.
    /// Instructs recurring, substantive mentions — not a Tools section roll-call.
    /// Includes URLs when available; instructs LLM to set Run.href on substantive mentions.
    /// </summary>
    private static void AppendKnownToolsBrief(StringBuilder sb, ProjectGenerationContext context)
    {
        var toolsByHeading = context.HierarchyToolsByHeading ?? Array.Empty<ToolsByHeading>();
        var allTools = toolsByHeading.SelectMany(g => g.Tools).Distinct(new ToolInfoComparer()).ToList();

        var toolSources = context.KeywordSources
            .Where(k => k.Category == KeywordSourceCategory.Tools)
            .ToList();

        if (allTools.Count == 0 && toolSources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== KNOWN TOOLS (grounding — weave into sections where contextually relevant) ===");
        sb.AppendLine(
            "Reference these tools by name wherever each is contextually relevant across sections — " +
            "recurring where relevant, discussed substantively. Do NOT create a dedicated Tools/Platforms " +
            "section, do NOT produce a roll-call list, and do NOT mention each tool only once at first occurrence.");

        var hasUrls = allTools.Any(t => !string.IsNullOrWhiteSpace(t.Href));
        if (hasUrls)
        {
            sb.AppendLine("When a tool in this list is substantively mentioned in body prose (not headings), set its Run object's " +
                "\"href\" field to the URL given below. Never fabricate a URL for a tool without a link. Avoid over-linking " +
                "(max ~1-2 linked mentions per tool per document).");
        }

        if (allTools.Count > 0)
        {
            sb.AppendLine("Hierarchy tools grouped by source section:");
            foreach (var group in toolsByHeading)
            {
                sb.AppendLine($"• {group.Heading}:");
                foreach (var tool in group.Tools)
                {
                    if (!string.IsNullOrWhiteSpace(tool.Href))
                    {
                        sb.AppendLine($"  - {tool.Name}: {tool.Href}");
                    }
                    else
                    {
                        sb.AppendLine($"  - {tool.Name}");
                    }
                }
            }
        }

        foreach (var source in toolSources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            if (!string.IsNullOrWhiteSpace(source.ExtractedToolResearchJson))
            {
                var json = source.ExtractedToolResearchJson!;
                if (json.Length > 2500) json = json[..2500] + "…";
                sb.AppendLine(json);
            }
            else
            {
                foreach (var h in source.Headings.Take(4)) sb.AppendLine($"- {h}");
                foreach (var p in source.Paragraphs.Take(2)) sb.AppendLine($"  {p}");
            }
        }
    }

    private static string FormatSourceLabel(KeywordSourceSummary source) =>
        !string.IsNullOrWhiteSpace(source.Title) ? source.Title! : source.SourceLabel;

    private sealed class ToolInfoComparer : IEqualityComparer<ToolInfo>
    {
        public bool Equals(ToolInfo? x, ToolInfo? y)
        {
            if (x is null || y is null) return x == y;
            return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ToolInfo obj) => obj.Name.ToLowerInvariant().GetHashCode();
    }
}
