using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Guardrail;

/// <summary>
/// LLM Pass-2 for guardrail <c>restructure</c> rules — v1's static <c>ContentGuardrail</c> only
/// flags these; this service rewrites the flagged section via a focused completion (v2-only).
/// Deferred Pass-2 for full-document restructure remains backlog; this targets one section at a time
/// inside VALIDATE's REPAIR loop.
/// </summary>
public sealed class GccV2RestructurePassService
{
    private readonly ILogger<GccV2RestructurePassService> _logger;

    public GccV2RestructurePassService(ILogger<GccV2RestructurePassService> logger) => _logger = logger;

    public async Task<(Section Section, int TokensUsed)> RewriteSectionAsync(
        Section section,
        IReadOnlyList<string> flaggedPhrases,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        CancellationToken ct)
    {
        var plain = GccV2OverlapGate.FlattenPlainText(section);
        var phraseList = flaggedPhrases.Count > 0
            ? string.Join(", ", flaggedPhrases.Select(p => $"\"{p}\""))
            : "(unspecified AI-filler / corporate jargon)";

        var system = """
            You rewrite ONE section of B2B content to remove flagged AI-filler or corporate jargon.
            Preserve the section's heading, factual claims, and assigned editorial job — change wording only.
            Respond with ONLY a single JSON section object matching this shape:
            {"tag":"h2","heading":string,"paragraphs":[{"type":"text","runs":[{"text":string}]}],"children":[]}
            No markdown fences, no commentary.
            """;

        var user = $"""
            Target keyword: {context.TargetKeyword}
            Section heading (keep exactly): {section.Heading}
            Flagged phrases to remove or replace with plain language: {phraseList}

            Current section plain text:
            {plain}
            """;

        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User, user),
            ],
            Temperature: 0.4,
            MaxOutputTokens: 2048);

        try
        {
            var result = await provider.CompleteAsync(request, ct);
            var parsed = LlmResponseJsonParser.ParseSection(result.Content, section.Tag ?? "h2", "restructure pass-2");
            var normalized = parsed with { Heading = section.Heading, Tag = section.Tag ?? "h2" };
            var tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
            return (normalized, tokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restructure pass-2 failed for section \"{Heading}\"; returning original.", section.Heading);
            return (section, 0);
        }
    }
}
