using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// The default <see cref="ISocialModelClient"/>: one call to the Anthropic Messages API with a forced
/// tool, read back as the tool's input. It is modeled on <see cref="AnthropicProvider"/> and uses the
/// same settings (<c>LlmProviders__Anthropic__*</c>, the key from <c>ANTHROPIC_API_KEY</c> when the
/// setting is empty) and the same <see cref="LlmConcurrencyGate"/>, so GeekSocial's calls count against
/// the one global limit.
/// </summary>
/// <remarks>
/// <para>
/// It is not a reuse of <see cref="AnthropicProvider"/> because that one logs the whole response body,
/// and here a body can quote another person's comment. This client logs what a failure was (status,
/// the API's error type, stop reason, block types, model, elapsed time) and never the prompt, the
/// comment or the answer.
/// </para>
/// <para>
/// It never throws to the agent: every failure is a failed Result and one Error log line. A cancelled
/// call (the service stopping) is the one exception, and propagates as cancellation. One attempt; no
/// retry, no second model.
/// </para>
/// </remarks>
public sealed class ClaudeSocialModelClient : ISocialModelClient
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string ModelSetting = "LlmProviders__Anthropic__Model";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly LlmConcurrencyGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClaudeSocialModelClient> _logger;
    private readonly Func<string, string?> _readEnvironment;

    public ClaudeSocialModelClient(
        HttpClient http,
        IOptions<LlmProvidersOptions> options,
        LlmConcurrencyGate gate,
        TimeProvider clock,
        ILogger<ClaudeSocialModelClient> logger)
        : this(http, options, gate, clock, logger, Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>For tests: reads environment variables through <paramref name="readEnvironment"/>.</summary>
    internal ClaudeSocialModelClient(
        HttpClient http,
        IOptions<LlmProvidersOptions> options,
        LlmConcurrencyGate gate,
        TimeProvider clock,
        ILogger<ClaudeSocialModelClient> logger,
        Func<string, string?> readEnvironment)
    {
        _http = http;
        _options = options.Value.Anthropic;
        _gate = gate;
        _clock = clock;
        _logger = logger;
        _readEnvironment = readEnvironment;
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<Result<JsonElement>> CallToolAsync(SocialModelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = new CallFacts(string.IsNullOrWhiteSpace(request.Purpose) ? "unnamed" : request.Purpose.Trim());

        if (string.IsNullOrWhiteSpace(request.ToolName)
            || string.IsNullOrWhiteSpace(request.UserMessage)
            || string.IsNullOrWhiteSpace(request.SystemPrompt)
            || request.MaxOutputTokens <= 0)
        {
            return Fail(call, SocialFailureCodes.InvalidRequest,
                "The model request needs a system prompt, a user turn, a tool name and a positive output limit.");
        }

        JsonNode? schema;
        try
        {
            schema = JsonNode.Parse(request.InputSchemaJson);
        }
        catch (JsonException)
        {
            schema = null;
        }

        if (schema is not JsonObject)
            return Fail(call, SocialFailureCodes.InvalidRequest, "The tool's input schema is not a JSON object.");

        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey) ? _readEnvironment(ApiKeyVariable) : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Fail(call, SocialFailureCodes.NotConfigured,
                $"No Anthropic API key is set. Set {ApiKeyVariable} (or LlmProviders__Anthropic__ApiKey). Nothing was sent.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
            return Fail(call, SocialFailureCodes.NotConfigured, $"No Anthropic model is set. Set {ModelSetting}. Nothing was sent.");

        call.Model = _options.Model;
        var payload = new MessagesRequest
        {
            Model = _options.Model,
            System = request.SystemPrompt,
            MaxTokens = request.MaxOutputTokens,
            Messages = [new MessagesTurn("user", request.UserMessage)],
            Tools = [new MessagesTool { Name = request.ToolName, Description = NullIfBlank(request.ToolDescription), InputSchema = schema }],
            ToolChoice = new MessagesToolChoice { Type = "tool", Name = request.ToolName },
        };

        var started = _clock.GetTimestamp();
        return await _gate.RunAsync(() => SendAsync(payload, request.ToolName, call, apiKey, started, ct), ct).ConfigureAwait(false);
    }

    private async Task<Result<JsonElement>> SendAsync(
        MessagesRequest payload, string toolName, CallFacts call, string apiKey, long started, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", _options.AnthropicVersion);

        string body;
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            call.Elapsed = _clock.GetElapsedTime(started);
            return Fail(call, SocialFailureCodes.ModelFailed, "The Anthropic API could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            call.Elapsed = _clock.GetElapsedTime(started);
            return Fail(call, SocialFailureCodes.ModelFailed, $"The Anthropic API did not answer within {_options.TimeoutSeconds} seconds.");
        }

        using (response)
        {
            try
            {
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                call.Elapsed = _clock.GetElapsedTime(started);
                call.Status = (int)response.StatusCode;
                return Fail(call, SocialFailureCodes.ModelFailed, "The Anthropic API's answer could not be read.");
            }

            call.Elapsed = _clock.GetElapsedTime(started);
            call.Status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                call.ErrorType = ErrorTypeOf(body);
                return Fail(call, SocialFailureCodes.ModelFailed,
                    $"The Anthropic API answered {(int)response.StatusCode} ({call.ErrorType ?? "no error type"}).");
            }
        }

        MessagesResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MessagesResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (parsed is null)
            return Fail(call, SocialFailureCodes.ModelFailed, "The Anthropic API answered 200 with a body that did not parse.");

        call.StopReason = parsed.StopReason;
        call.Blocks = parsed.Content.Count == 0 ? "none" : string.Join(", ", parsed.Content.Select(c => c.Type));
        if (!string.IsNullOrEmpty(parsed.Model))
            call.Model = parsed.Model;

        if (parsed.StopReason is "refusal" or "max_tokens")
            return Fail(call, SocialFailureCodes.ModelFailed, $"The model stopped with {parsed.StopReason}, so its answer is not complete.");

        var tool = parsed.Content.FirstOrDefault(c => c.Type == "tool_use" && c.Name == toolName);
        if (tool?.Input is not { ValueKind: JsonValueKind.Object } input)
            return Fail(call, SocialFailureCodes.ModelFailed, $"The model did not answer with the {toolName} tool.");

        return Result<JsonElement>.Success(input.Clone());
    }

    /// <summary>The API's own error type (for example overloaded_error), and nothing else from the body.</summary>
    private static string? ErrorTypeOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private Result<JsonElement> Fail(CallFacts call, string code, string reason)
    {
        _logger.LogError(
            "Social model call {Purpose} failed ({Code}): {Reason} Model {Model}, HTTP {Status}, error type {ErrorType}, stop reason {StopReason}, blocks {Blocks}, {ElapsedMs} ms.",
            call.Purpose, code, reason, call.Model ?? "not set", call.Status?.ToString() ?? "none",
            call.ErrorType ?? "none", call.StopReason ?? "none", call.Blocks ?? "none",
            (long)call.Elapsed.TotalMilliseconds);
        return Result<JsonElement>.Failure(reason, code);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>What a log line may say about one call. It holds no text from the request or the answer.</summary>
    private sealed class CallFacts(string purpose)
    {
        public string Purpose { get; } = purpose;
        public string? Model { get; set; }
        public int? Status { get; set; }
        public string? ErrorType { get; set; }
        public string? StopReason { get; set; }
        public string? Blocks { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    private sealed class MessagesRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public List<MessagesTurn> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("tools")] public List<MessagesTool>? Tools { get; set; }
        [JsonPropertyName("tool_choice")] public MessagesToolChoice? ToolChoice { get; set; }
    }

    private sealed record MessagesTurn(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class MessagesTool
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("input_schema")] public JsonNode? InputSchema { get; set; }
    }

    private sealed class MessagesToolChoice
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "tool";
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class MessagesResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public List<MessagesBlock> Content { get; set; } = new();
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
    }

    private sealed class MessagesBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("input")] public JsonElement? Input { get; set; }
    }
}
