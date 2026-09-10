using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Optional ROI projection pin stored inside grid ConfigJson.</summary>
public static class GccV2GridRoiBinding
{
    public static readonly JsonSerializerOptions JsonOpts = GccV2GridSchedule.JsonOpts;

    public record State(
        string? RunId,
        string? ArtifactVersionId,
        string? ArtifactType,
        double? ExpectedRoiPercent,
        string? AttachedAtUtc);

    public static State Read(string? configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (!doc.RootElement.TryGetProperty("roiProjection", out var roi)
                || roi.ValueKind != JsonValueKind.Object)
                return new State(null, null, null, null, null);

            var runId = roi.TryGetProperty("runId", out var run) && run.ValueKind == JsonValueKind.String
                ? run.GetString()
                : null;
            var artifactVersionId = roi.TryGetProperty("artifactVersionId", out var ver)
                && ver.ValueKind == JsonValueKind.String
                ? ver.GetString()
                : null;
            var artifactType = roi.TryGetProperty("artifactType", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
            double? expectedRoiPercent = null;
            if (roi.TryGetProperty("expectedRoiPercent", out var pct)
                && pct.ValueKind == JsonValueKind.Number
                && pct.TryGetDouble(out var value))
                expectedRoiPercent = value;
            var attachedAtUtc = roi.TryGetProperty("attachedAtUtc", out var at)
                && at.ValueKind == JsonValueKind.String
                ? at.GetString()
                : null;
            return new State(runId, artifactVersionId, artifactType, expectedRoiPercent, attachedAtUtc);
        }
        catch (JsonException)
        {
            return new State(null, null, null, null, null);
        }
    }

    public static string Merge(string? configJson, State? binding)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                root[prop.Name] = prop.Value.Clone();
        }

        if (binding is null
            || string.IsNullOrWhiteSpace(binding.RunId)
            || string.IsNullOrWhiteSpace(binding.ArtifactVersionId))
        {
            root.Remove("roiProjection");
        }
        else
        {
            root["roiProjection"] = new
            {
                runId = binding.RunId,
                artifactVersionId = binding.ArtifactVersionId,
                artifactType = binding.ArtifactType ?? "roiProjection.v1",
                expectedRoiPercent = binding.ExpectedRoiPercent,
                attachedAtUtc = binding.AttachedAtUtc ?? DateTimeOffset.UtcNow.ToString("O"),
            };
        }

        return JsonSerializer.Serialize(root, JsonOpts);
    }
}
