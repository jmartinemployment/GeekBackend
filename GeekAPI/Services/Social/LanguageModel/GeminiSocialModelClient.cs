using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Google's Gemini <c>models/{model}:generateContent</c>, asked for JSON that matches the answer's
/// schema (<c>generationConfig.responseMimeType</c> and <c>responseJsonSchema</c>). Only a candidate
/// that finished with STOP is read; a blocked prompt, a safety stop or a cut-off answer is a failure.
/// Parts a thinking model marks as thoughts are not part of the answer.
/// </summary>
public sealed class GeminiSocialModelClient : SocialModelClientBase
{
    public GeminiSocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger<GeminiSocialModelClient> logger)
        : base(http, settings, gate, clock, logger)
    {
    }

    protected override HttpRequestMessage BuildRequest(SocialModelRequest request, JsonObject schema)
    {
        var payload = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }),
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.UserMessage }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = schema.DeepClone(),
                ["maxOutputTokens"] = request.MaxOutputTokens,
            },
        };

        var address = $"{Settings.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(Settings.Model)}:generateContent";
        var message = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonBody(payload) };
        message.Headers.Add("x-goog-api-key", Settings.ApiKey);
        return message;
    }

    protected override Result<ModelReply> ReadReply(JsonElement root, SocialModelRequest request, CallFacts call)
    {
        var served = StringOf(root, "modelVersion");
        if (!string.IsNullOrEmpty(served))
            call.Model = served;

        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.ValueKind == JsonValueKind.Object
            && StringOf(feedback, "blockReason") is { } blockReason)
        {
            call.StopReason = blockReason;
            call.Output = "none";
            return Result<ModelReply>.Failure($"The request was blocked ({blockReason}), so the model gave no answer.", SocialFailureCodes.ModelFailed);
        }

        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            call.Output = "none";
            return Result<ModelReply>.Failure("The model's answer has no candidates.", SocialFailureCodes.ModelFailed);
        }

        var candidate = candidates[0];
        call.StopReason = StringOf(candidate, "finishReason");
        var parts = candidate.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("parts", out var partList)
            && partList.ValueKind == JsonValueKind.Array
                ? partList.EnumerateArray().ToList()
                : new List<JsonElement>();
        call.Output = parts.Count == 0
            ? "none"
            : string.Join(", ", parts.Select(p => IsThought(p) ? "thought" : p.TryGetProperty("text", out _) ? "text" : "other"));

        if (call.StopReason != "STOP")
        {
            return Result<ModelReply>.Failure(
                $"The model stopped with {call.StopReason ?? "no finish reason"}, so its answer is not complete.", SocialFailureCodes.ModelFailed);
        }

        var text = new StringBuilder();
        foreach (var part in parts.Where(p => !IsThought(p)))
        {
            if (StringOf(part, "text") is { } piece)
                text.Append(piece);
        }

        var answer = ParseAnswerText(text.ToString());
        if (!answer.IsSuccess)
            return Result<ModelReply>.Failure(answer.Error!, SocialFailureCodes.ModelFailed);

        // A thinking model's thoughts are billed as output, so they count toward the reply's size.
        var output = TokenCount(root, "usageMetadata", "candidatesTokenCount") + TokenCount(root, "usageMetadata", "thoughtsTokenCount");
        return Result<ModelReply>.Success(new ModelReply
        {
            Answer = answer.Value,
            ServedModel = served,
            InputTokens = TokenCount(root, "usageMetadata", "promptTokenCount"),
            OutputTokens = output,
        });
    }

    private static bool IsThought(JsonElement part) =>
        part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;
}
