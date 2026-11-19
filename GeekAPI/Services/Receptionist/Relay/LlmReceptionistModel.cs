using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.Social;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// <see cref="IReceptionistModel"/> over GeekAPI's configured default LLM provider (<see cref="ReceptionistModelSettings"/>):
/// Anthropic's Messages API, or the Chat Completions contract OpenAI and Groq share, with the same addresses and headers GeekAPI's
/// other model clients use. Unlike them it never logs a response body or anything sent: a failure is logged and thrown with the
/// provider and HTTP status only, because what a caller said must not reach a log. Each request declares the one tool the script names,
/// <see cref="EndCallTool"/>, so the model hangs up by calling it rather than by saying its name to the caller.
/// </summary>
public sealed class LlmReceptionistModel : IReceptionistModel
{
    /// <summary>A spoken reply is a few sentences; this caps what one reply can cost.</summary>
    internal const int MaxOutputTokens = 1024;

    /// <summary>The tool the receptionist's script tells the model to call when the call is over.</summary>
    internal const string EndCallTool = "end_call";

    private const string EndCallDescription =
        "Hang up the call. Call it only once the caller has been thanked and has nothing more to ask.";

    private readonly HttpClient _http;
    private readonly ReceptionistModelSettings _settings;
    private readonly ILogger<LlmReceptionistModel> _logger;

    public LlmReceptionistModel(HttpClient http, ReceptionistModelSettings settings, ILogger<LlmReceptionistModel> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        if (settings.Model is { } model)
            _http.Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds);
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<ReceptionistReply> ReplyAsync(string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default)
    {
        var model = _settings.Model
            ?? throw new ReceptionistModelException($"The receptionist's model is not configured: {_settings.Reason}");
        var provider = model.Provider!;

        using var request = provider == SocialModelProviders.Anthropic
            ? AnthropicRequest(model.BaseUrl, model.ApiKey, model.AnthropicVersion, model.Model, systemPrompt, turns)
            : ChatCompletionsRequest(model.BaseUrl, model.ApiKey, model.Model, systemPrompt, turns);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("The receptionist's model ({Provider}) could not be reached.", provider);
            throw new ReceptionistModelException($"{provider} could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("The receptionist's model ({Provider}) did not answer within {Seconds} seconds.", provider, model.TimeoutSeconds);
            throw new ReceptionistModelException($"{provider} did not answer in time.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("The receptionist's model ({Provider}) answered {Status}.", provider, (int)response.StatusCode);
                throw new ReceptionistModelException($"{provider} answered {(int)response.StatusCode}.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            }
            catch (JsonException ex)
            {
                _logger.LogError("The receptionist's model ({Provider}) answered 200 with a body that is not JSON.", provider);
                throw new ReceptionistModelException($"{provider} answered with a body that is not JSON.", ex);
            }

            using (document)
            {
                var text = provider == SocialModelProviders.Anthropic
                    ? AnthropicText(document.RootElement)
                    : ChatCompletionsText(document.RootElement);
                var tools = provider == SocialModelProviders.Anthropic
                    ? AnthropicTools(document.RootElement)
                    : ChatCompletionsTools(document.RootElement);

                var unknown = tools.FirstOrDefault(name => name != EndCallTool);
                if (unknown is not null)
                {
                    _logger.LogError("The receptionist's model ({Provider}) called a tool it was not given.", provider);
                    throw new ReceptionistModelException($"{provider} called a tool the receptionist does not have.");
                }

                var endCall = tools.Count > 0;
                if (string.IsNullOrWhiteSpace(text) && !endCall)
                {
                    _logger.LogError("The receptionist's model ({Provider}) answered 200 with no usable reply.", provider);
                    throw new ReceptionistModelException($"{provider} answered with no usable reply.");
                }

                return new ReceptionistReply(text?.Trim() ?? "", endCall);
            }
        }
    }

    internal static HttpRequestMessage AnthropicRequest(
        string url, string key, string version, string model, string systemPrompt, IReadOnlyList<ReceptionistTurn> turns)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["system"] = systemPrompt,
            ["max_tokens"] = MaxOutputTokens,
            ["messages"] = Messages(turns),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = EndCallTool,
                ["description"] = EndCallDescription,
                ["input_schema"] = NoArguments(),
            }),
        };
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(payload) };
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", version);
        return message;
    }

    internal static HttpRequestMessage ChatCompletionsRequest(
        string url, string key, string model, string systemPrompt, IReadOnlyList<ReceptionistTurn> turns)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = systemPrompt });
        foreach (var turn in Messages(turns))
            messages.Add(turn!.DeepClone());

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["max_completion_tokens"] = MaxOutputTokens,
            ["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = EndCallTool,
                    ["description"] = EndCallDescription,
                    ["parameters"] = NoArguments(),
                },
            }),
        };
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(payload) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return message;
    }

    private static JsonArray Messages(IReadOnlyList<ReceptionistTurn> turns)
    {
        var messages = new JsonArray();
        foreach (var turn in turns)
        {
            messages.Add(new JsonObject
            {
                ["role"] = turn.Speaker == ReceptionistSpeaker.Caller ? "user" : "assistant",
                ["content"] = turn.Text,
            });
        }
        return messages;
    }

    private static JsonObject NoArguments() => new() { ["type"] = "object", ["properties"] = new JsonObject() };

    private static StringContent Json(JsonObject payload) => new(payload.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>The text blocks of an Anthropic reply, joined; null when there are none.</summary>
    internal static string? AnthropicText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "text"
                && block.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                text.Append(value.GetString());
        }
        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>The names of the tools an Anthropic reply calls (its tool_use blocks); empty when it calls none.</summary>
    internal static IReadOnlyList<string> AnthropicTools(JsonElement root)
    {
        var names = new List<string>();
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return names;
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "tool_use")
                names.Add(block.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "");
        }
        return names;
    }

    /// <summary>The first choice's message content of a Chat Completions reply; null when there is none.</summary>
    internal static string? ChatCompletionsText(JsonElement root)
    {
        if (FirstMessage(root) is not { } message || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
            return null;
        return content.GetString();
    }

    /// <summary>The names of the functions the first choice's message calls (its tool_calls); empty when it calls none.</summary>
    internal static IReadOnlyList<string> ChatCompletionsTools(JsonElement root)
    {
        var names = new List<string>();
        if (FirstMessage(root) is not { } message || !message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return names;
        foreach (var call in calls.EnumerateArray())
        {
            names.Add(call.TryGetProperty("function", out var function) && function.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                ? name.GetString()!
                : "");
        }
        return names;
    }

    private static JsonElement? FirstMessage(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;
        return choices[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object ? message : null;
    }
}
