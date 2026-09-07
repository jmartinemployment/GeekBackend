using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.Workflow.Providers;

/// <summary>Talks to the OpenAI Chat Completions API (https://api.openai.com/v1/chat/completions).</summary>
public class OpenAiProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiProvider> _logger;

    public LlmProviderType ProviderType => LlmProviderType.OpenAi;

    public OpenAiProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.OpenAi;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            : _options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentGenerationException(
                "OpenAI API key is not configured. Set OPENAI_API_KEY (or LlmProviders__OpenAi__ApiKey).");
        }

        var model = request.Model ?? _options.Model;
        var reasoning = IsReasoningModel(model);
        var payload = new OpenAiCompatibleRequest
        {
            Model = model,
            Messages = request.Messages.Select(m => new OpenAiCompatibleMessage(m.RoleString, m.Content)).ToList(),
            Temperature = reasoning ? null : request.Temperature,
            MaxTokens = reasoning ? null : request.MaxOutputTokens,
            MaxCompletionTokens = reasoning ? request.MaxOutputTokens : null,
            ResponseFormat = request.JsonSchema is null
                ? null
                : new OpenAiResponseFormat
                {
                    JsonSchema = new OpenAiJsonSchemaSpec
                    {
                        Name = request.JsonSchemaName ?? "response",
                        Strict = true,
                        Schema = System.Text.Json.Nodes.JsonNode.Parse(request.JsonSchema),
                    },
                },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException("Could not reach the OpenAI API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"OpenAI request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiCompatibleResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("OpenAI returned an empty/unparseable response.");

        var choice = parsed.Choices.FirstOrDefault()
            ?? throw new ContentGenerationException("OpenAI response contained no choices.");

        var cachedTokens = parsed.Usage?.PromptTokensDetails?.CachedTokens;
        _logger.LogInformation(
            "OpenAI usage: promptTokens={PromptTokens} cachedTokens={CachedTokens} completionTokens={CompletionTokens}",
            parsed.Usage?.PromptTokens, cachedTokens, parsed.Usage?.CompletionTokens);

        return new ChatCompletionResult(
            Content: choice.Message.Content,
            ModelUsed: parsed.Model ?? _options.Model,
            PromptTokens: parsed.Usage?.PromptTokens,
            CompletionTokens: parsed.Usage?.CompletionTokens,
            CachedTokens: cachedTokens);
    }

    /// <summary>o1/o3/o4-family models reject custom temperature and want max_completion_tokens.</summary>
    internal static bool IsReasoningModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.Trim().ToLowerInvariant();
        return m.StartsWith("o1", StringComparison.Ordinal)
               || m.StartsWith("o3", StringComparison.Ordinal)
               || m.StartsWith("o4", StringComparison.Ordinal);
    }
}
