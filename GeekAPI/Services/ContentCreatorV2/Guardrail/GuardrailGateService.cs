using GeekAPI.Services.ContentCreatorV2.Geo;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Guardrail;

/// <summary>
/// Hard VALIDATE gate for v2: apply DB-backed guardrail strip/replace, then run
/// <see cref="GcwSeoAnalyzer"/> and <see cref="GcwPolishAnalyzer"/> (called, never edited) plus the
/// v2-only <see cref="GccV2GeoAnalyzer"/> for AI-visibility readiness. GEO is advisory — it never
/// contributes to <see cref="GuardrailShipReady"/>/<see cref="AnalyzerShipReady"/>.
/// </summary>
public sealed record GuardrailGateResult(
    ContentDocument CleanedDocument,
    int GuardrailFlaggedCount,
    int GuardrailRestructureCount,
    IReadOnlyList<string> GuardrailRestructurePhrases,
    GcwSeoAnalyzer.SeoReport Seo,
    GcwPolishAnalyzer.PolishReport Polish,
    GccV2GeoAnalyzer.GeoReport Geo)
{
    public bool GuardrailShipReady => GuardrailRestructureCount == 0;
    public bool AnalyzerShipReady => Polish.ShipReady;
}

public sealed class GuardrailGateService
{
    private readonly GccV2GuardrailService _guardrail;

    public GuardrailGateService(GccV2GuardrailService guardrail) => _guardrail = guardrail;

    public async Task<GuardrailGateResult> EvaluateAsync(
        ContentDocument document,
        string targetKeyword,
        string? contentType,
        CancellationToken ct)
    {
        var guardrail = await _guardrail.ApplyAsync(document, contentType, ct);
        var analyzerJson = GccV2AnalyzerDocument.Serialize(guardrail.Document);
        var seo = GcwSeoAnalyzer.Analyze(analyzerJson, targetKeyword);
        var polish = GcwPolishAnalyzer.Analyze(analyzerJson, Array.Empty<string>());
        var geo = GccV2GeoAnalyzer.Analyze(analyzerJson, targetKeyword);
        return new GuardrailGateResult(
            guardrail.Document,
            guardrail.FlaggedCount,
            guardrail.RestructureCount,
            guardrail.RestructurePhrases,
            seo,
            polish,
            geo);
    }
}
