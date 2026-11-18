using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Perplexity's Agent API (<c>POST /v1/agent</c>), which replaced its Sonar chat completions on
/// 2026-09-27. The model is named in <c>provider/model</c> form (for example <c>openai/...</c>), so
/// Perplexity passes the text on to that model's company as well.
/// </summary>
/// <remarks>
/// <para>
/// No web search. Perplexity's migration guide (read 2026-11-18) says search runs when the
/// <c>web_search</c> tool is offered or an <c>effort</c> level is chosen, and that an effort level's
/// tools cannot be turned off. So this client names a <c>model</c>, never an effort, and offers no tools.
/// </para>
/// <para>
/// That is checked, not assumed: an answer whose output holds anything but a message (search results,
/// a fetched page, a tool call) is a failure, and nothing it returned is used. Comment text must not
/// become a search query.
/// </para>
/// </remarks>
public sealed class PerplexitySocialModelClient : SocialModelClientBase
{
    public PerplexitySocialModelClient(
        HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger<PerplexitySocialModelClient> logger)
        : base(http, settings, gate, clock, logger)
    {
    }

    protected override HttpRequestMessage BuildRequest(SocialModelRequest request, JsonObject schema)
    {
        var payload = new JsonObject
        {
            ["model"] = Settings.Model,
            ["instructions"] = request.SystemPrompt,
            ["input"] = request.UserMessage,
            ["max_output_tokens"] = request.MaxOutputTokens,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.AnswerName,
                    ["schema"] = schema.DeepClone(),
                },
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

        call.StopReason = StringOf(root, "status");
        var items = root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array
            ? output.EnumerateArray().ToList()
            : new List<JsonElement>();
        call.Output = items.Count == 0 ? "none" : string.Join(", ", items.Select(i => StringOf(i, "type") ?? "untyped"));

        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            return Result<ModelReply>.Failure("The response reports an error.", SocialFailureCodes.ModelFailed);

        if (call.StopReason != "completed")
        {
            return Result<ModelReply>.Failure(
                $"The response is {call.StopReason ?? "without a status"}, so its answer is not complete.", SocialFailureCodes.ModelFailed);
        }

        var tooled = items.Select(i => StringOf(i, "type")).FirstOrDefault(type => type != "message");
        if (items.Any(i => StringOf(i, "type") != "message"))
        {
            return Result<ModelReply>.Failure(
                $"The response holds {tooled ?? "an untyped item"} although no tool was offered, so nothing it returned is used.",
                SocialFailureCodes.ModelFailed);
        }

        var text = new StringBuilder();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (StringOf(part, "type") == "output_text" && StringOf(part, "text") is { } piece)
                    text.Append(piece);
            }
        }

        var answer = ParseAnswerText(text.ToString());
        if (!answer.IsSuccess)
            return Result<ModelReply>.Failure(answer.Error!, SocialFailureCodes.ModelFailed);

        return Result<ModelReply>.Success(new ModelReply
        {
            Answer = answer.Value,
            ServedModel = served,
            InputTokens = TokenCount(root, "usage", "input_tokens"),
            OutputTokens = TokenCount(root, "usage", "output_tokens"),
        });
    }
}
