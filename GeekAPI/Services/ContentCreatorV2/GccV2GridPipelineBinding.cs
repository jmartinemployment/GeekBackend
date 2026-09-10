using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Optional Geek Content Pipeline binding stored inside grid ConfigJson.</summary>
public static class GccV2GridPipelineBinding
{
    public static readonly JsonSerializerOptions JsonOpts = GccV2GridSchedule.JsonOpts;

    public record State(string? PipelineDefinitionId, string? LastPipelineRunId);

    public static State Read(string? configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new State(null, null);
            var pipelineDefinitionId = doc.RootElement.TryGetProperty("pipelineDefinitionId", out var def)
                && def.ValueKind == JsonValueKind.String
                && Guid.TryParse(def.GetString(), out _)
                ? def.GetString()
                : null;
            var lastPipelineRunId = doc.RootElement.TryGetProperty("lastPipelineRunId", out var run)
                && run.ValueKind == JsonValueKind.String
                && Guid.TryParse(run.GetString(), out _)
                ? run.GetString()
                : null;
            return new State(pipelineDefinitionId, lastPipelineRunId);
        }
        catch (JsonException)
        {
            return new State(null, null);
        }
    }

    public static string Merge(string? configJson, State binding)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                root[prop.Name] = prop.Value.Clone();
        }

        if (string.IsNullOrWhiteSpace(binding.PipelineDefinitionId))
            root.Remove("pipelineDefinitionId");
        else
            root["pipelineDefinitionId"] = binding.PipelineDefinitionId;

        if (string.IsNullOrWhiteSpace(binding.LastPipelineRunId))
            root.Remove("lastPipelineRunId");
        else
            root["lastPipelineRunId"] = binding.LastPipelineRunId;

        return JsonSerializer.Serialize(root, JsonOpts);
    }
}
