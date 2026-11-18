using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Chat Completions with a strict JSON-schema response format, the contract OpenAI and Groq share
/// (the same <c>response_format</c> GeekAPI's <c>OpenAiCompatibleDtos</c> sends for Content Creator).
/// Strict mode makes the provider hold the answer to the schema; a model that cannot do strict mode is
/// refused by the provider, and that refusal is the failure the person sees.
/// </summary>
public abstract class OpenAiCompatibleSocialModelClient : SocialModelClientBase
{
    protected OpenAiCompatibleSocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger logger)
        : base(http, settings, gate, clock, logger)
    {
    }

    protected override HttpRequestMessage BuildRequest(SocialModelRequest request, JsonObject schema)
    {
        var jsonSchema = new JsonObject
        {
            ["name"] = request.AnswerName,
            ["schema"] = schema.DeepClone(),
            ["strict"] = true,
        };
        if (!string.IsNullOrWhiteSpace(request.AnswerDescription))
            jsonSchema["description"] = request.AnswerDescription;

        var payload = new JsonObject
        {
            ["model"] = Settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserMessage }),
            ["max_completion_tokens"] = request.MaxOutputTokens,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = jsonSchema,
            },
        };

        var message = new HttpRequestMessage(HttpMethod.Post, Settings.BaseUrl) { Content = JsonBody(payload) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
        return message;
    }

    protected override Result<ModelReply> ReadReply(JsonElement root, SocialModelRequest request, CallFacts call)
    {
        var served = StringOf(root, "model");
        if (!string.IsNullOrEmpty(served))
            call.Model = served;

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            call.Output = "none";
            return Result<ModelReply>.Failure("The model's answer has no choices.", SocialFailureCodes.ModelFailed);
        }

        var choice = choices[0];
        call.StopReason = StringOf(choice, "finish_reason");
        call.Output = "message";

        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            return Result<ModelReply>.Failure("The model's answer has no message.", SocialFailureCodes.ModelFailed);

        if (!string.IsNullOrWhiteSpace(StringOf(message, "refusal")))
            return Result<ModelReply>.Failure("The model refused to answer.", SocialFailureCodes.ModelFailed);

        if (call.StopReason != "stop")
        {
            return Result<ModelReply>.Failure(
                $"The model stopped with {call.StopReason ?? "no finish reason"}, so its answer is not complete.", SocialFailureCodes.ModelFailed);
        }

        var answer = ParseAnswerText(StringOf(message, "content"));
        if (!answer.IsSuccess)
            return Result<ModelReply>.Failure(answer.Error!, SocialFailureCodes.ModelFailed);

        return Result<ModelReply>.Success(new ModelReply
        {
            Answer = answer.Value,
            ServedModel = served,
            InputTokens = TokenCount(root, "usage", "prompt_tokens"),
            OutputTokens = TokenCount(root, "usage", "completion_tokens"),
        });
    }
}

/// <summary>OpenAI's Chat Completions (the models behind ChatGPT).</summary>
public sealed class OpenAiSocialModelClient : OpenAiCompatibleSocialModelClient
{
    public OpenAiSocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger<OpenAiSocialModelClient> logger)
        : base(http, settings, gate, clock, logger)
    {
    }
}

/// <summary>
/// Groq's OpenAI-compatible Chat Completions. Groq enforces strict schemas only on some models
/// (console.groq.com/docs/structured-outputs, read 2026-11-18); with any other model Groq refuses the
/// request, and that is the failure the person sees.
/// </summary>
public sealed class GroqSocialModelClient : OpenAiCompatibleSocialModelClient
{
    public GroqSocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger<GroqSocialModelClient> logger)
        : base(http, settings, gate, clock, logger)
    {
    }
}
