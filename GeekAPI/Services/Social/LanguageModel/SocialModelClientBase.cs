using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// What every provider's client does the same way: check the request, refuse to send without a key or
/// a model, make one call through the shared <see cref="LlmConcurrencyGate"/>, turn every failure into a
/// failed Result with one Error log line, and hand the provider's own reading of the answer back as a
/// <see cref="SocialModelAnswer"/>. Each subclass builds its provider's request and reads its answer.
/// </summary>
/// <remarks>
/// <para>
/// No body is ever logged, because a body can quote another person's comment. A log line says what a
/// failure was (status, the API's error type, stop reason, the kinds of output, model, elapsed time)
/// and never the prompt, the comment or the answer; a failure's message is built from those facts only.
/// </para>
/// <para>
/// One attempt, no retry, no second model and no second provider. A cancelled call (the service
/// stopping) propagates as cancellation; everything else is a failed Result.
/// </para>
/// </remarks>
public abstract class SocialModelClientBase : ISocialModelClient
{
    private const int MaxAnswerNameLength = 64;

    private readonly HttpClient _http;
    private readonly LlmConcurrencyGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    protected SocialModelClientBase(HttpClient http, SocialModelSettings settings, LlmConcurrencyGate gate, TimeProvider clock, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Provider is null)
            throw new ArgumentException("A provider's client needs settings that name a provider.", nameof(settings));

        _http = http;
        Settings = settings;
        _gate = gate;
        _clock = clock;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    }

    protected SocialModelSettings Settings { get; }

    public string? Provider => Settings.Provider;

    public string? Model => string.IsNullOrWhiteSpace(Settings.Model) ? null : Settings.Model;

    /// <summary>The provider's name in a failure's message, for example "the OpenAI API".</summary>
    protected string ApiName => $"the {SocialModelProviders.DisplayName(Settings.Provider!)} API";

    public async Task<Result<SocialModelAnswer>> AnswerAsync(SocialModelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = new CallFacts(string.IsNullOrWhiteSpace(request.Purpose) ? "unnamed" : request.Purpose.Trim());

        if (string.IsNullOrWhiteSpace(request.UserMessage)
            || string.IsNullOrWhiteSpace(request.SystemPrompt)
            || request.MaxOutputTokens <= 0)
        {
            return Fail(call, SocialFailureCodes.InvalidRequest,
                "The model request needs a system prompt, a user turn and a positive output limit.");
        }

        if (!IsAnswerName(request.AnswerName))
        {
            return Fail(call, SocialFailureCodes.InvalidRequest,
                $"The answer's name must be 1 to {MaxAnswerNameLength} letters and digits, so every provider accepts it.");
        }

        JsonNode? schema;
        try
        {
            schema = JsonNode.Parse(request.AnswerSchemaJson);
        }
        catch (JsonException)
        {
            schema = null;
        }

        if (schema is not JsonObject schemaObject)
            return Fail(call, SocialFailureCodes.InvalidRequest, "The answer's schema is not a JSON object.");

        // The reason reaches the post form, so it names the provider and not the setting; the log line
        // names the setting for the operator.
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            return Fail(call, SocialFailureCodes.NotConfigured,
                $"No {SocialModelProviders.DisplayName(Settings.Provider!)} API key is set on GeekAPI. Nothing was sent.", Settings.KeyVariable);
        }

        if (string.IsNullOrWhiteSpace(Settings.Model))
        {
            return Fail(call, SocialFailureCodes.NotConfigured,
                $"No {SocialModelProviders.DisplayName(Settings.Provider!)} model is set on GeekAPI. Nothing was sent.", Settings.ModelVariable);
        }

        call.Model = Settings.Model;
        var started = _clock.GetTimestamp();
        return await _gate.RunAsync(() => SendAsync(request, schemaObject, call, started, ct), ct).ConfigureAwait(false);
    }

    private async Task<Result<SocialModelAnswer>> SendAsync(
        SocialModelRequest request, JsonObject schema, CallFacts call, long started, CancellationToken ct)
    {
        using var httpRequest = BuildRequest(request, schema);

        string body;
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            call.Elapsed = _clock.GetElapsedTime(started);
            return Fail(call, SocialFailureCodes.ModelFailed, $"{Capitalized(ApiName)} could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            call.Elapsed = _clock.GetElapsedTime(started);
            return Fail(call, SocialFailureCodes.ModelFailed, $"{Capitalized(ApiName)} did not answer within {Settings.TimeoutSeconds} seconds.");
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
                return Fail(call, SocialFailureCodes.ModelFailed, $"{Capitalized(ApiName)}'s answer could not be read.");
            }

            call.Elapsed = _clock.GetElapsedTime(started);
            call.Status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                call.ErrorType = ErrorTypeOf(body);
                return Fail(call, SocialFailureCodes.ModelFailed,
                    $"{Capitalized(ApiName)} answered {(int)response.StatusCode} ({call.ErrorType ?? "no error type"}).");
            }
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Fail(call, SocialFailureCodes.ModelFailed, $"{Capitalized(ApiName)} answered {call.Status} with a body that did not parse.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Fail(call, SocialFailureCodes.ModelFailed, $"{Capitalized(ApiName)} answered {call.Status} with a body that is not an object.");

            var read = ReadReply(document.RootElement, request, call);
            if (!read.IsSuccess)
                return Fail(call, SocialFailureCodes.ModelFailed, read.Error!);

            var reply = read.Value!;
            return Result<SocialModelAnswer>.Success(new SocialModelAnswer
            {
                Answer = reply.Answer.Clone(),
                Provider = Settings.Provider!,
                Model = string.IsNullOrWhiteSpace(reply.ServedModel) ? Settings.Model : reply.ServedModel,
                InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens,
            });
        }
    }

    /// <summary>The provider's request: address, headers and body. The body may carry comment text and is never logged.</summary>
    protected abstract HttpRequestMessage BuildRequest(SocialModelRequest request, JsonObject schema);

    /// <summary>
    /// Reads a 200 answer. A failure's message must be built from the answer's facts (a stop reason, an
    /// output kind), never from its text, because the caller logs it.
    /// </summary>
    protected abstract Result<ModelReply> ReadReply(JsonElement root, SocialModelRequest request, CallFacts call);

    /// <summary>The API's own error type from an error body, and nothing else from it.</summary>
    protected virtual string? ErrorTypeOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "type", "status", "code" })
                {
                    if (error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>A JSON request body. Built from nodes, so every value in it is escaped by the serializer.</summary>
    protected static StringContent JsonBody(JsonObject payload) =>
        new(payload.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads the text a provider returned as the structured answer. Anything but one JSON object is a
    /// failure that says so without quoting the text.
    /// </summary>
    protected static Result<JsonElement> ParseAnswerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result<JsonElement>.Failure("The model's answer is empty.", SocialFailureCodes.ModelFailed);

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? Result<JsonElement>.Success(doc.RootElement.Clone())
                : Result<JsonElement>.Failure("The model's answer is JSON but not an object.", SocialFailureCodes.ModelFailed);
        }
        catch (JsonException)
        {
            return Result<JsonElement>.Failure("The model's answer is not JSON.", SocialFailureCodes.ModelFailed);
        }
    }

    /// <summary>A whole number from an answer's usage block, or 0 when the provider did not give it.</summary>
    protected static int TokenCount(JsonElement parent, string usageProperty, string countProperty)
    {
        return parent.TryGetProperty(usageProperty, out var usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty(countProperty, out var count)
            && count.TryGetInt32(out var value)
                ? value
                : 0;
    }

    protected static string? StringOf(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsAnswerName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxAnswerNameLength && name.All(char.IsAsciiLetterOrDigit);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private Result<SocialModelAnswer> Fail(CallFacts call, string code, string reason, string? setting = null)
    {
        _logger.LogError(
            "Social model call {Purpose} to {Provider} failed ({Code}): {Reason} Setting {Setting}, model {Model}, HTTP {Status}, error type {ErrorType}, stop reason {StopReason}, output {Output}, {ElapsedMs} ms.",
            call.Purpose, Settings.Provider, code, reason, setting ?? "none", call.Model ?? "not set", call.Status?.ToString() ?? "none",
            call.ErrorType ?? "none", call.StopReason ?? "none", call.Output ?? "none",
            (long)call.Elapsed.TotalMilliseconds);
        return Result<SocialModelAnswer>.Failure(reason, code);
    }

    /// <summary>A provider's answer once read: the structured answer, the model that served it, and its size.</summary>
    protected sealed class ModelReply
    {
        public JsonElement Answer { get; init; }
        public string? ServedModel { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
    }

    /// <summary>What a log line may say about one call. It holds no text from the request or the answer.</summary>
    protected sealed class CallFacts(string purpose)
    {
        public string Purpose { get; } = purpose;
        public string? Model { get; set; }
        public int? Status { get; set; }
        public string? ErrorType { get; set; }
        public string? StopReason { get; set; }

        /// <summary>The kinds of output the answer held (for example "tool_use" or "message, search_results").</summary>
        public string? Output { get; set; }

        public TimeSpan Elapsed { get; set; }
    }
}
