using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Extracts groundedness and schema-validity signals from task-agent artifact payloads
/// for portfolio quality telemetry (rates only — not dollar ROI).
/// </summary>
internal static class GccV2ArtifactQualityMetrics
{
    public sealed record Sample(
        int GroundedHits,
        int GroundedTotal,
        int SchemaValidHits,
        int SchemaValidTotal);

    public sealed record Aggregate(
        int RunsSampled,
        int GroundedHits,
        int GroundedTotal,
        double? GroundednessRate,
        int SchemaValidHits,
        int SchemaValidTotal,
        double? SchemaValidityRate,
        string Message);

    public static Sample FromPayload(string? payloadJson, string? validationState = null)
    {
        var groundedHits = 0;
        var groundedTotal = 0;
        var schemaHits = 0;
        var schemaTotal = 0;

        if (!string.IsNullOrWhiteSpace(validationState))
        {
            schemaTotal += 1;
            if (string.Equals(validationState, "valid", StringComparison.OrdinalIgnoreCase))
                schemaHits += 1;
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Sample(groundedHits, groundedTotal, schemaHits, schemaTotal);

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            CountGroundedFlags(root, "sections", ref groundedHits, ref groundedTotal);
            CountVerificationStatuses(root, "claims", ref groundedHits, ref groundedTotal);
            CountVerificationStatuses(root, "pairs", ref groundedHits, ref groundedTotal);
            CountVerificationStatuses(root, "faqs", ref groundedHits, ref groundedTotal);
            CountSchemaValidation(root, ref schemaHits, ref schemaTotal);
        }
        catch (JsonException)
        {
            // Malformed payload contributes only ValidationState if present.
        }

        return new Sample(groundedHits, groundedTotal, schemaHits, schemaTotal);
    }

    public static Aggregate Combine(IEnumerable<Sample> samples)
    {
        var list = samples.ToList();
        var groundedHits = list.Sum(x => x.GroundedHits);
        var groundedTotal = list.Sum(x => x.GroundedTotal);
        var schemaHits = list.Sum(x => x.SchemaValidHits);
        var schemaTotal = list.Sum(x => x.SchemaValidTotal);
        double? groundedRate = groundedTotal > 0
            ? Math.Round(groundedHits / (double)groundedTotal, 4)
            : null;
        double? schemaRate = schemaTotal > 0
            ? Math.Round(schemaHits / (double)schemaTotal, 4)
            : null;
        var message = groundedTotal == 0 && schemaTotal == 0
            ? "No groundedness or schema-validity signals in the lookback window."
            : "Rates are workflow quality outcomes from TaskRun artifacts — not cash ROI.";
        return new Aggregate(
            list.Count,
            groundedHits,
            groundedTotal,
            groundedRate,
            schemaHits,
            schemaTotal,
            schemaRate,
            message);
    }

    private static void CountGroundedFlags(
        JsonElement root, string arrayName, ref int hits, ref int total)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            return;
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("grounded", out var grounded)) continue;
            if (grounded.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            total += 1;
            if (grounded.ValueKind == JsonValueKind.True) hits += 1;
        }
    }

    private static void CountVerificationStatuses(
        JsonElement root, string arrayName, ref int hits, ref int total)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            return;
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("verificationStatus", out var statusEl)
                || statusEl.ValueKind != JsonValueKind.String)
                continue;
            var status = statusEl.GetString()?.Trim().ToLowerInvariant();
            if (status is not ("supported" or "unsupported" or "unverifiable")) continue;
            total += 1;
            if (status == "supported") hits += 1;
        }
    }

    private static void CountSchemaValidation(
        JsonElement root, ref int hits, ref int total)
    {
        if (!root.TryGetProperty("validation", out var array) || array.ValueKind != JsonValueKind.Array)
            return;
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("valid", out var valid)) continue;
            if (valid.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            total += 1;
            if (valid.ValueKind == JsonValueKind.True) hits += 1;
        }
    }
}
