using System.Text.Json;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreatorV2.Transforms;

public sealed record GccV2TransformVariant(
    string Channel,
    string Title,
    string? Headline,
    string Body,
    string? Cta,
    IReadOnlyList<string> Hashtags,
    string ContentDocumentJson);

public sealed record GccV2TransformResult(IReadOnlyList<GccV2TransformVariant> Variants);

/// <summary>
/// Repurpose a v2 generate-job draft into channel variants using <see cref="GcwRepurposeCatalog"/>.
/// Same output channel mix for every allowed source type (pillar, blog, tool, email, social, ads).
/// </summary>
public sealed class GccV2RepurposeTransformService
{
    public async Task<GccV2TransformResult> ApplyAsync(
        string sourceContentType,
        string sourceDocumentJson,
        IReadOnlyList<string>? channels,
        IContentGenerationProvider provider,
        CancellationToken ct,
        IReadOnlyDictionary<string, int>? countOverrides = null)
    {
        var typeGuidance = GccV2RepurposeSourceTypes.GuidanceFor(sourceContentType);
        var channelBrief = GcwRepurposeCatalog.BuildChannelBrief(channels, countOverrides);
        var userBrief =
            typeGuidance + " Adapt it into the channel slots below — do not assume the source is a pillar article.\n\n" +
            "Produce ONE pack JSON for ONLY the channel slots below (one variant object per slot, same order). " +
            "Shape: { \"variants\": [ { \"channel\": string, \"title\": string, \"headline\": string|null, " +
            "\"body\": string, \"cta\": string|null, \"hashtags\": string[]|null } ] }. Reply with valid JSON only.\n\n" +
            channelBrief + "\n\nSource content:\n" + sourceDocumentJson;

        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, "You write marketing channel packs as strict JSON only."),
                new ChatMessage(ChatRole.User, userBrief),
            ],
            Temperature: 0.4);

        var result = await provider.CompleteAsync(request, ct);
        var raw = result.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Re-Purpose LLM returned empty content.");

        var pack = GcwRepurposePack.Parse(raw);
        var variants = pack.Variants.Select(v => new GccV2TransformVariant(
            v.Channel,
            v.Title,
            v.Headline,
            v.Body,
            v.Cta,
            v.Hashtags,
            GcwRepurposePack.ToContentDocumentJson(v))).ToList();

        return new GccV2TransformResult(variants);
    }
}
