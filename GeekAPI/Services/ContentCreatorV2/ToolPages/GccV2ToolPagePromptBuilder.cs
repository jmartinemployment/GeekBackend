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
    private const string SectionJsonContract =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [{\"type\":\"text\",\"runs\":[{\"text\": string, \"bold\": boolean, \"italic\": boolean, \"href\": null}, ...]} " +
        "OR {\"type\":\"list\",\"ordered\":boolean,\"items\":[[{\"text\": string, \"bold\": boolean, \"italic\": boolean, \"href\": null}, ...], ...]}], " +
        "\"href\": null, \"children\": [Section, ...]}";

    private const string SectionsArrayJsonContract =
        "{\"sections\": [" + SectionJsonContract + ", ...] (top-level h2 sections, in order)}";

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

    public ChatCompletionRequest BuildPartnerToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string toolSlug,
        string? extractedToolResearchJson,
        string? pillarBodyExcerpt)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            .AppendLine("Respond with ONLY the sections array for this tool overview page — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("Required top-level (h2) sections, in order: Overview, Key Capabilities, Implementation Considerations, When to Use.")
            .AppendLine($"Target at least {ContentLengthTargets.ToolMinWords:N0} words.")
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent features.")
            .AppendLine("Do NOT include Sources, blockquotes, or external links — attribution is added by the pipeline.")
            .AppendLine($"Tie Overview and When to Use to this project's use-case ({context.TargetKeyword}).")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword context: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Public path: /tools/{GccV2ToolSlugHelper.DefaultDepartment}/{toolSlug}");
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
            MaxOutputTokens: 8192);
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

    public ChatCompletionRequest BuildOverviewBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        IReadOnlyList<(string Name, string OnSiteHref)> partnerLinks,
        string? pillarBodyExcerpt)
    {
        var perPlatformTarget =
            $"{ContentLengthTargets.PillarToolsSectionMinWords / Math.Max(partnerLinks.Count, 1)}" +
            $"-{ContentLengthTargets.PillarToolsSectionTargetMaxWords / Math.Max(partnerLinks.Count, 1)}";

        var partnerBlock = partnerLinks.Count == 0
            ? "(no partner tools resolved — write a keyword-only overview without a tools index section)"
            : string.Join("\n", partnerLinks.Select(p => $"- {p.Name} → {p.OnSiteHref}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write a keyword use-case overview page for the target keyword — NOT a single-product page.")
            .AppendLine("Respond with ONLY a sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("Required top-level (h2) sections in order:")
            .AppendLine("  1. Overview — what this use case means for teams adopting AI")
            .AppendLine("  2. Capabilities — what good solutions in this space typically enable")
            .AppendLine("  3. Implementation — how an implementer approaches rollout for this use case")
            .AppendLine("  4. When to Use — decision criteria for pursuing this use case")
            .AppendLine($"  5. \"{toolsSectionHeading}\" — tools index with one h3 per partner tool")
            .AppendLine("For each tools-index h3: heading MUST be plain tool name only (no HTML). First paragraph must mention the tool and its role; include an on-site path only as plain text like \"See /tools/marketing/slug\" — do NOT use href runs or external URLs.")
            .AppendLine($"Each platform subsection targets ~{perPlatformTarget} words — richer than a pillar mention, not a full tool page copy.")
            .AppendLine("Never use external partner URLs anywhere in this page.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Page title context: {metadata.Title}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .AppendLine("Partner on-site links (write richer blurbs; link targets are on-site only):")
            .AppendLine(partnerBlock);
        if (!string.IsNullOrWhiteSpace(pillarBodyExcerpt))
        {
            user.AppendLine("=== PILLAR EXCERPT (ground framing; do not reprint) ===");
            user.AppendLine(pillarBodyExcerpt);
        }

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 8192);
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

    public ChatCompletionRequest BuildSourceExcerptPrompt(string toolName, string sourceUrl, string researchJson)
    {
        var system =
            "Write 1-3 sentences paraphrasing the supplied tool research for a blockquote attribution block. " +
            "Respond with ONLY plain text — no markdown, no HTML, no quotes wrapping the whole answer.";
        var user = new StringBuilder()
            .AppendLine($"Tool: {toolName}")
            .AppendLine($"Source URL: {sourceUrl}")
            .AppendLine("Research JSON:")
            .AppendLine(researchJson)
            .ToString();
        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.3,
            MaxOutputTokens: 256);
    }

    private static string TruncateExcerpt(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
