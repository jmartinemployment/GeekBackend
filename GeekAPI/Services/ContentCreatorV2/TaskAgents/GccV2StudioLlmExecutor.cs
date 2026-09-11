using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Builds Studio LLM prompts and customAgentOutput.v1 payloads for durable task-agent runs.
/// Dry-run stays template-only; generation happens only on claimed runs.
/// </summary>
internal static class GccV2StudioLlmExecutor
{
    public const string Methodology = "studio-llm.v1";

    public static ChatCompletionRequest BuildRequest(
        string renderedInstructions,
        string exampleOutput,
        string evaluationPrompt,
        double temperature,
        string? model,
        int maxOutputTokens = 4096)
    {
        var user = new System.Text.StringBuilder();
        user.AppendLine("## Instructions");
        user.AppendLine(renderedInstructions.Trim());
        user.AppendLine();
        if (!string.IsNullOrWhiteSpace(exampleOutput))
        {
            user.AppendLine("## Example output shape");
            user.AppendLine(exampleOutput.Trim());
            user.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(evaluationPrompt))
        {
            user.AppendLine("## Evaluation criteria");
            user.AppendLine(evaluationPrompt.Trim());
            user.AppendLine();
        }
        user.AppendLine("## Produce");
        user.Append("Return only the final agent output. Do not invent governed brand facts that are not present in the instructions or inputs.");

        return new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(
                    ChatRole.System,
                    "You execute a Custom Agent Studio instruction template. Follow the rendered instructions exactly. "
                    + "When an example output is supplied, match its format and structure. "
                    + "Do not claim measured demand, rankings, or traffic unless the instructions provide those facts."),
                new ChatMessage(ChatRole.User, user.ToString()),
            ],
            Temperature: ClampTemperature(temperature),
            MaxOutputTokens: Math.Clamp(maxOutputTokens, 256, 8192),
            Model: string.IsNullOrWhiteSpace(model) ? null : model.Trim());
    }

    public static string BuildArtifactJson(
        string renderedInstructions,
        string exampleOutput,
        string evaluationPrompt,
        JsonElement inputs,
        string generatedOutput,
        string modelUsed,
        IReadOnlyList<string>? warnings = null)
    {
        return GccV2CanonicalJson.Serialize(new
        {
            artifactType = GccV2StudioTemplateRenderer.ArtifactType,
            methodology = Methodology,
            renderedInstructions,
            exampleOutput,
            evaluationPrompt,
            generatedOutput,
            modelUsed,
            inputs,
            warnings = warnings ?? Array.Empty<string>(),
        });
    }

    public static string? FirstAllowedModel(string? allowedModelsJson)
    {
        if (string.IsNullOrWhiteSpace(allowedModelsJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(allowedModelsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() == 0)
                return null;
            var first = document.RootElement[0];
            return first.ValueKind == JsonValueKind.String ? first.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static double ReadTemperature(JsonElement workflow, double fallback = 0.2)
    {
        if (workflow.TryGetProperty("temperature", out var temperature)
            && temperature.ValueKind == JsonValueKind.Number
            && temperature.TryGetDouble(out var value))
            return ClampTemperature(value);
        return fallback;
    }

    public static string ReadString(JsonElement workflow, string property)
    {
        if (workflow.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";
        return "";
    }

    private static double ClampTemperature(double temperature) =>
        Math.Clamp(temperature, 0, 2);
}
