using System.Text;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>
/// Copied tool-page prompts from workflow <c>ContentPromptBuilder</c> — v2-owned, never calls
/// <c>IContentPromptBuilder</c> tool methods.
/// </summary>
public sealed class GccV2ToolPagePromptBuilder
{
    public static readonly string[] PartnerSectionHeadings =
    [
        "Overview",
        "Key Capabilities",
        "Implementation Considerations",
        "When to Use",
    ];

    private const string SectionJsonContract =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [{\"type\":\"text\",\"runs\":[{\"text\": string, \"bold\": boolean, \"italic\": boolean, \"href\": null}, ...]} " +
        "OR {\"type\":\"list\",\"ordered\":boolean,\"items\":[[{\"text\": string, \"bold\": boolean, \"italic\": boolean, \"href\": null}, ...], ...]}], " +
        "\"href\": null, \"children\": [Section, ...]}";

    private const string ToolMetadataJsonContract =
        "{\"departmentListExcerpt\": string, \"summary\": string, \"mainSummary\": string, \"heroSummary\": string, " +
        "\"homeSummary\": string, \"blogSummary\": string, \"toolPageExcerpt\": string, \"advertisingSummary\": string, " +
        "\"metaDescription\": string (max 160 chars)}";

    public ChatCompletionRequest BuildToolResearchExtractionPrompt(string fileName, string htmlOrText)
    {
        var clipped = htmlOrText.Length > 40_000 ? htmlOrText[..40_000] + "…" : htmlOrText;
        var system =
            "Extract structured research about a single software tool from the uploaded page. " +
            "Respond with ONLY JSON (no fences): " +
            "{\"name\": string, \"summary\": string, \"whatItDoes\": string, \"features\": string[], " +
            "\"useCases\": string[], \"positioning\": string, \"pricing\": string}. " +
            "Use empty string/array when unknown — never invent pricing or features not supported by the page.";
        var user = $"File: {fileName}\n\n---\n{clipped}";
        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.1,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildPartnerToolSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string toolSlug,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        string? extractedToolResearchJson,
        string? pillarBodyExcerpt)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            .AppendLine("This page is published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no markdown fences, no commentary:")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\". Include 2-3 h3 subsections in \"children\" with substantive paragraphs and at least one list where appropriate.")
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent features.")
            .AppendLine("Do NOT include Sources, blockquotes, or external links — attribution is added by the pipeline.")
            .AppendLine(PartnerSectionGuidance(sectionHeading, context, app.Name))
            .AppendLine("There is no real case-study data available — never present a named client as if it were real.")
            .AppendLine($"Tie Overview and When to Use to this project's use-case ({context.TargetKeyword}).")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ToolBody,
                $"Write the \"{sectionHeading}\" section of the tool page for {app.Name}."))
            .AppendLine()
            .AppendLine($"Target keyword context: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Public path: /tools/{GccV2ToolSlugHelper.DefaultDepartment}/{toolSlug}")
            .AppendLine($"Write section {sectionIndex + 1} of {totalSections}: \"{sectionHeading}\".");
        if (!string.IsNullOrWhiteSpace(pillarBodyExcerpt))
        {
            user.AppendLine("=== PILLAR USE-CASE EXCERPT ===");
            user.AppendLine(pillarBodyExcerpt);
        }

        if (!string.IsNullOrWhiteSpace(extractedToolResearchJson))
        {
            user.AppendLine("=== PERSISTED TOOL RESEARCH (authoritative) ===");
            user.AppendLine(extractedToolResearchJson);
        }

        user.AppendLine($"Write expert third-person technical prose focused on {app.Name}, grounded in this use-case.");
        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.5,
            MaxOutputTokens: 4096);
    }

    public ChatCompletionRequest BuildOverviewSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        string? pillarBodyExcerpt)
    {
        var outlineContext = string.Join("\n", fullOutline.Select((h, i) => $"{i + 1}. {h}"));
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONE section of a keyword use-case overview page — NOT a single-product tool page.")
            .AppendLine("Third person, expert, consultative — frame the use case for teams adopting AI in this space.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no markdown fences, no commentary:")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\". Include 2-3 h3 children with multiple paragraphs; at least one list paragraph where appropriate.")
            .AppendLine("PROBLEM-FIRST when this section establishes the core practitioner problem: open on cost, delay, risk, or wasted effort before naming solutions.")
            .AppendLine("ADVANCE sections must add new ground — do not repeat the same pain point or fix from earlier sections.")
            .AppendLine("Never use external partner URLs. Never write a product roundup — this is a use-case page.")
            .AppendLine($"Target {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words for this section.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
            .AppendLine($"Write section {sectionIndex + 1} of {totalSections}: \"{sectionHeading}\".")
            .AppendLine($"Page title context: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine()
            .AppendLine("Full page outline (context only — write ONLY this section):")
            .AppendLine(outlineContext);
        if (!string.IsNullOrWhiteSpace(pillarBodyExcerpt))
        {
            user.AppendLine("=== PILLAR EXCERPT (ground framing; do not reprint) ===");
            user.AppendLine(pillarBodyExcerpt);
        }

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 4096);
    }

    public ChatCompletionRequest BuildOverviewPartnerChildPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        string platformName,
        IReadOnlyList<string> allPlatforms,
        int platformIndex,
        int platformCount,
        string? extractedToolResearchJson,
        string onSiteHref)
    {
        var perPlatformTarget =
            $"{ContentLengthTargets.PillarToolsSectionMinWords / Math.Max(platformCount, 1)}" +
            $"-{ContentLengthTargets.PillarToolsSectionTargetMaxWords / Math.Max(platformCount, 1)}";

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONE platform subsection for the Tools index of a keyword overview page — third person, expert, consultative.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no markdown fences, no commentary:")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h3\". Heading must be plain tool name only (no HTML, no links in heading).")
            .AppendLine("Include: overview paragraph of what the platform does for this use case, then a list with 2-4 factual capability bullets from the research.")
            .AppendLine($"Then one child Section (tag h4, heading \"How an AI implementer helps with {platformName}\").")
            .AppendLine($"Target ~{perPlatformTarget} words — richer than a pillar mention, not a full tool page copy.")
            .AppendLine("Never invent features; ground claims in persisted research. No external URLs — on-site path is applied by the pipeline.")
            .AppendLine("CRITICAL: no real case-study data — never present a named client as if it were real.")
            .ToString();

        var platformList = string.Join(", ", allPlatforms.Select((p, i) => i == platformIndex ? $"[{p}]" : p));
        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
            .AppendLine($"Write Tools platform {platformIndex + 1} of {platformCount}: \"{platformName}\".")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .AppendLine($"On-site path (mention as plain text only, e.g. See {onSiteHref}): {onSiteHref}")
            .AppendLine($"Platforms in this Tools section (write ONLY the bracketed one): {platformList}");
        if (!string.IsNullOrWhiteSpace(extractedToolResearchJson))
        {
            user.AppendLine("=== PERSISTED TOOL RESEARCH (authoritative) ===");
            user.AppendLine(extractedToolResearchJson);
        }

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.45,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildPartnerToolMetadataPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        ContentDocument body)
    {
        var system = new StringBuilder()
            .AppendLine("You write presentation metadata for a B2B tool overview page (schema.org SoftwareApplication).")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(ToolMetadataJsonContract)
            .AppendLine("Each summary field must use different wording.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine()
            .AppendLine("Tool page body (for context):")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildOverviewMetadataPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription)
    {
        const string contract =
            "{\"summary\": string, \"mainSummary\": string, \"heroSummary\": string, \"homeSummary\": string, " +
            "\"blogSummary\": string, \"advertisingSummary\": string, \"metaDescription\": string (max 160 chars)}";

        var system = new StringBuilder()
            .AppendLine("You write presentation metadata for a keyword tool-overview hub page.")
            .AppendLine("Respond with ONLY JSON — no markdown fences:")
            .AppendLine(contract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Title: {title}")
            .AppendLine($"Existing metaDescription hint: {metaDescription ?? "(none)"}")
            .AppendLine("Body excerpt:")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildSourceQuotePrompt(
        string toolName,
        string sourceUrl,
        string sourcePageText)
    {
        var system =
            "Select ONE verbatim sentence or short passage from the source page text below for a blockquote. " +
            "Copy exact wording from the page — do NOT paraphrase, summarize, or rewrite. " +
            "Respond with ONLY the quoted words — no markdown, no HTML, no surrounding quote marks (they are added by the pipeline).";
        var user = new StringBuilder()
            .AppendLine($"Tool: {toolName}")
            .AppendLine($"Source URL: {sourceUrl}")
            .AppendLine("=== SOURCE PAGE TEXT (copy verbatim from here only) ===")
            .AppendLine(sourcePageText)
            .ToString();
        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.1,
            MaxOutputTokens: 256);
    }

    private static string PartnerSectionGuidance(string sectionHeading, ProjectGenerationContext context, string toolName)
    {
        if (sectionHeading.Contains("Implementation", StringComparison.OrdinalIgnoreCase))
        {
            return new StringBuilder()
                .AppendLine($"Target ~450-600 words for Implementation Considerations — made concrete to {toolName}:")
                .AppendLine($"  1. Accelerated deployment — what shortens go-live for {toolName}.")
                .AppendLine($"  2. Data model design — {toolName}-specific mapping decisions.")
                .AppendLine($"  3. Workflow/process configuration — routing, approval chains, automation for {toolName}.")
                .AppendLine($"  4. Custom code/development — {toolName}'s extension mechanism if any; if config-only, say so.")
                .AppendLine($"Frame through {context.PublisherName} ({context.ImplementerPositioning}) closing the gap for a client.")
                .ToString();
        }

        if (sectionHeading.Contains("Capabilities", StringComparison.OrdinalIgnoreCase))
            return $"Target ~400-550 words. Cover verifiable {toolName} capabilities from research — grouped thematically with h3 children.";

        if (sectionHeading.Contains("Overview", StringComparison.OrdinalIgnoreCase))
            return $"Target ~350-450 words. Establish what {toolName} is and why it matters for {context.TargetKeyword} — use-case grounded, not marketing fluff.";

        if (sectionHeading.Contains("When to Use", StringComparison.OrdinalIgnoreCase))
            return $"Target ~300-400 words. Decision criteria for when teams pursuing {context.TargetKeyword} should evaluate {toolName}.";

        return $"Target ~350-500 words for \"{sectionHeading}\" — substantive, research-grounded prose about {toolName}.";
    }

    private static string TruncateExcerpt(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
