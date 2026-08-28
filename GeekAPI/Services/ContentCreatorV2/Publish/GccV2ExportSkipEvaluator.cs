using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

/// <summary>Export skip reasons — extracted for unit tests (§5.4 / §5.5).</summary>
public static class GccV2ExportSkipEvaluator
{
    private static readonly JsonSerializerOptions ResultJsonOpts = CreateResultJsonOpts();

    private static JsonSerializerOptions CreateResultJsonOpts()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    public static string? TryGetSkipReason(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            return "No completed result yet.";

        try
        {
            var payload = JsonSerializer.Deserialize<ExportPayload>(resultJson, ResultJsonOpts);
            if (payload?.Document is null)
                return "No document in result.";
        }
        catch (JsonException)
        {
            return "Result could not be parsed.";
        }

        return null;
    }

    private sealed record ExportPayload(string? Title, ContentDocument? Document);
}
