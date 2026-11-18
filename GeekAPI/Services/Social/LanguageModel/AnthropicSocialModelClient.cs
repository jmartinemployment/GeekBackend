using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Anthropic's Messages API with a forced tool: the answer's schema is the tool's input schema, and the
/// tool's input is the answer. Modeled on <see cref="AnthropicProvider"/>, which is not reused because it
/// logs the whole response body, and here a body can quote another person's comment.
/// </summary>
public sealed class AnthropicSocialModelClient : SocialModelClientBase
{
    public AnthropicSocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger<AnthropicSocialModelClient> logger)
        : base(http, settings, gate, clock, logger)
    {
    }

    protected override HttpRequestMessage BuildRequest(SocialModelRequest request, JsonObject schema)
    {
        var tool = new JsonObject
        {
            ["name"] = request.AnswerName,
            ["input_schema"] = schema.DeepClone(),
        };
        if (!string.IsNullOrWhiteSpace(request.AnswerDescription))
            tool["description"] = request.AnswerDescription;

        var payload = new JsonObject
        {
            ["model"] = Settings.Model,
            ["system"] = request.SystemPrompt,
            ["max_tokens"] = request.MaxOutputTokens,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.UserMessage }),
            ["tools"] = new JsonArray(tool),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = request.AnswerName },
        };

        var message = new HttpRequestMessage(HttpMethod.Post, Settings.BaseUrl) { Content = JsonBody(payload) };
        message.Headers.Add("x-api-key", Settings.ApiKey);
        message.Headers.Add("anthropic-version", Settings.AnthropicVersion);
        return message;
    }

    protected override Result<ModelReply> ReadReply(JsonElement root, SocialModelRequest request, CallFacts call)
    {
        var served = StringOf(root, "model");
        if (!string.IsNullOrEmpty(served))
            call.Model = served;

        call.StopReason = StringOf(root, "stop_reason");
        var blocks = root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray().ToList()
            : new List<JsonElement>();
        call.Output = blocks.Count == 0
            ? "none"
            : string.Join(", ", blocks.Select(b => StringOf(b, "type") ?? "untyped"));

        if (call.StopReason is "refusal" or "max_tokens")
            return Result<ModelReply>.Failure($"The model stopped with {call.StopReason}, so its answer is not complete.", SocialFailureCodes.ModelFailed);

        var tool = blocks.FirstOrDefault(b => StringOf(b, "type") == "tool_use" && StringOf(b, "name") == request.AnswerName);
        if (tool.ValueKind != JsonValueKind.Object
            || !tool.TryGetProperty("input", out var input)
            || input.ValueKind != JsonValueKind.Object)
        {
            return Result<ModelReply>.Failure($"The model did not answer with the {request.AnswerName} tool.", SocialFailureCodes.ModelFailed);
        }

        return Result<ModelReply>.Success(new ModelReply
        {
            Answer = input.Clone(),
            ServedModel = served,
            InputTokens = TokenCount(root, "usage", "input_tokens"),
            OutputTokens = TokenCount(root, "usage", "output_tokens"),
        });
    }
}
