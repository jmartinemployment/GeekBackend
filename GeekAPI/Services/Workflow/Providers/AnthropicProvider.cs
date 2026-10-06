using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.Workflow.Providers;

/// <summary>Talks to the Anthropic Messages API (https://api.anthropic.com/v1/messages).</summary>
public class AnthropicProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicProvider> _logger;

    public LlmProviderType ProviderType => LlmProviderType.Anthropic;

    public AnthropicProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Anthropic;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            : _options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentGenerationException(
                "Anthropic API key is not configured. Set ANTHROPIC_API_KEY (or LlmProviders__Anthropic__ApiKey).");
        }

        // Anthropic's Messages API takes system prompts as a top-level field, not a message with role "system".
        var systemPrompt = string.Join("\n\n", request.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content));

        var turnMessages = request.Messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new AnthropicMessage(m.RoleString, m.Content))
            .ToList();

        // Resolved once. ModelUsed below reports what was SENT, and deriving it a second time is how
        // the two come to disagree when a request carries its own Model.
        var model = ProviderModelGuard.Require(
            request.Model ?? _options.Model, "LlmProviders__Anthropic__Model");

        var payload = new AnthropicRequest
        {
            Model = model,
            System = string.IsNullOrEmpty(systemPrompt) ? null : systemPrompt,
            Messages = turnMessages,
            MaxTokens = request.MaxOutputTokens,
            // NOT sent. Anthropic's current models reject it outright --
            // 400 invalid_request_error, "`temperature` is deprecated for this model" -- and that is
            // a hard failure of every call, not a degraded one: on 2026-10-03 it took all four
            // long-form types and every one of 108 partner extraction pages across five partners,
            // which surfaced as "0 of 5 partners can be grounded" rather than as a provider fault.
            //
            // request.Temperature is honoured by OpenAI and Groq, which still take it. It is dropped
            // here rather than conditioned on the model name: a name check is a second place to keep
            // current, and the models that accept it are the ones being retired.
            Temperature = null,
        };

        // Anthropic has no response_format/json_schema mode — structured output is done via forced
        // tool-use: define a single tool whose input_schema is the desired shape, force tool_choice
        // to it, and read the already-parsed JSON back out of the tool_use content block's "input"
        // (not a string to parse — the API returns it as a real JSON value).
        var usingSchema = request.JsonSchema is not null;
        if (usingSchema)
        {
            var schemaName = request.JsonSchemaName ?? "response";
            payload.Tools =
            [
                new AnthropicTool
                {
                    Name = schemaName,
                    InputSchema = System.Text.Json.Nodes.JsonNode.Parse(request.JsonSchema!),
                },
            ];
            payload.ToolChoice = new AnthropicToolChoice { Type = "tool", Name = schemaName };
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", _options.AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException("Could not reach the Anthropic API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"Anthropic request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("Anthropic returned an empty/unparseable response.");

        string content;
        if (usingSchema)
        {
            var toolUseBlock = parsed.Content.FirstOrDefault(c => c.Type == "tool_use")
                ?? throw NoBlock("tool_use", parsed, body);
            // Input is already a parsed JSON value (not a string) — GetRawText() hands back the
            // exact JSON text the API returned, with no extra serialization round-trip.
            content = toolUseBlock.Input?.GetRawText() ?? string.Empty;
        }
        else
        {
            var textBlock = parsed.Content.FirstOrDefault(c => c.Type == "text")
                ?? throw NoBlock("text", parsed, body);
            content = textBlock.Text ?? string.Empty;
        }

        return new ChatCompletionResult(
            Content: content,
            ModelUsed: parsed.Model ?? model,
            PromptTokens: parsed.Usage?.InputTokens,
            CompletionTokens: parsed.Usage?.OutputTokens);
    }

    /// <summary>
    /// A 200 whose content has no block of the kind the call needs. The failure says what did come
    /// back -- the stop reason and the block types -- and the body is logged whole, as a non-2xx
    /// body is.
    /// </summary>
    /// <remarks>
    /// On 2026-10-06 every piece of a Generate failed with "Anthropic response contained no text
    /// content block" after a 200 that took 25 seconds, and that sentence was all the log held: the
    /// body was read and thrown away, so whether the model refused, ran out of output tokens, or
    /// answered in a block type this code does not read could not be told after the fact.
    /// </remarks>
    private ContentGenerationException NoBlock(string wanted, AnthropicResponse parsed, string body)
    {
        var blocks = parsed.Content.Count == 0
            ? "no content blocks"
            : $"{parsed.Content.Count} content block(s): {string.Join(", ", parsed.Content.Select(c => c.Type))}";
        var stop = string.IsNullOrEmpty(parsed.StopReason) ? "not stated" : parsed.StopReason;
        _logger.LogError(
            "Anthropic returned 200 with no {Wanted} block (stop_reason {StopReason}; {Blocks}): {Body}",
            wanted, stop, blocks, body);
        return new ContentGenerationException(
            $"Anthropic response contained no {wanted} content block (stop_reason: {stop}; {blocks}).");
    }

    private sealed class AnthropicRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public List<AnthropicMessage> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        /// <summary>
        /// Omitted when null, and it is always null today -- see the assignment site for why.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("temperature")] public double? Temperature { get; set; }
        [JsonPropertyName("tools")] public List<AnthropicTool>? Tools { get; set; }
        [JsonPropertyName("tool_choice")] public AnthropicToolChoice? ToolChoice { get; set; }
    }

    private sealed class AnthropicTool
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("input_schema")] public System.Text.Json.Nodes.JsonNode? InputSchema { get; set; }
    }

    private sealed class AnthropicToolChoice
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "auto";
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public List<AnthropicContentBlock> Content { get; set; } = new();
        /// <summary>end_turn, max_tokens, stop_sequence, tool_use, pause_turn or refusal.</summary>
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
        [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("input")] public JsonElement? Input { get; set; }
    }

    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
