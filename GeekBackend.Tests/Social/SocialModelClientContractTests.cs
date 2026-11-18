using System.Net;
using System.Text.Json;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The contract every AI provider's client keeps, run against each one over a fake HTTP handler: a
/// structured answer comes back with who served it and its size; a refusal, a cut-off answer, an
/// answer that is not a JSON object and an error status each fail; a missing key or model sends
/// nothing; there is exactly one attempt; and no log line or error message holds the prompt, the
/// comment or the answer. Each provider is proven live only once its key is set; until then, these
/// are the evidence.
/// </summary>
public class SocialModelClientContractTests
{
    private const string Comment = "PRIVATE-COMMENT-TEXT from Jane at 12 Oak Street";
    private const string Echo = "PRIVATE-ECHO-IN-THE-ANSWER";
    private const string GoodAnswer = """{"reply":"Thanks for asking."}""";

    public static TheoryData<string> Providers => new(SocialModelProviders.All);

    /// <summary>A client, the handler it sends through, and every line it logged.</summary>
    private sealed record Built(ISocialModelClient Client, ScriptedModelHandler Handler, Func<IReadOnlyList<(LogLevel Level, string Message)>> Log);

    private static Dictionary<string, string?> Settings(string provider, string apiKey = "test-key", string model = "test-model")
    {
        var section = SocialModelProviders.SettingsSection(provider);
        return new Dictionary<string, string?>
        {
            [SocialModelSettings.ProviderVariable] = provider,
            [$"LlmProviders__{section}__ApiKey"] = apiKey,
            [$"LlmProviders__{section}__Model"] = model,
            [$"LlmProviders__{section}__BaseUrl"] = $"https://{provider}.test/endpoint",
            [$"LlmProviders__{section}__TimeoutSeconds"] = "30",
        };
    }

    private static Built Build(string provider, Func<HttpRequestMessage, string?, HttpResponseMessage> answer, string apiKey = "test-key", string model = "test-model")
    {
        var values = Settings(provider, apiKey, model);
        var settings = SocialModelSettings.From(name => values.GetValueOrDefault(name));
        var handler = new ScriptedModelHandler(answer);
        var http = new HttpClient(handler);
        var gate = new LlmConcurrencyGate(2);
        switch (provider)
        {
            case SocialModelProviders.Anthropic:
            {
                var log = new RecordingLog<AnthropicSocialModelClient>();
                return new Built(new AnthropicSocialModelClient(http, settings, gate, TimeProvider.System, log), handler, () => Lines(log));
            }
            case SocialModelProviders.OpenAi:
            {
                var log = new RecordingLog<OpenAiSocialModelClient>();
                return new Built(new OpenAiSocialModelClient(http, settings, gate, TimeProvider.System, log), handler, () => Lines(log));
            }
            case SocialModelProviders.Groq:
            {
                var log = new RecordingLog<GroqSocialModelClient>();
                return new Built(new GroqSocialModelClient(http, settings, gate, TimeProvider.System, log), handler, () => Lines(log));
            }
            case SocialModelProviders.Gemini:
            {
                var log = new RecordingLog<GeminiSocialModelClient>();
                return new Built(new GeminiSocialModelClient(http, settings, gate, TimeProvider.System, log), handler, () => Lines(log));
            }
            case SocialModelProviders.Perplexity:
            {
                var log = new RecordingLog<PerplexitySocialModelClient>();
                return new Built(new PerplexitySocialModelClient(http, settings, gate, TimeProvider.System, log), handler, () => Lines(log));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    private static IReadOnlyList<(LogLevel Level, string Message)> Lines<T>(RecordingLog<T> log) =>
        log.Entries.Select(e => (e.Level, e.Message)).ToList();

    private static SocialModelRequest Request(string schema = """{"type":"object","properties":{"reply":{"type":"string"}},"required":["reply"],"additionalProperties":false}""", string name = "writeReply") => new()
    {
        Purpose = "draft a reply",
        SystemPrompt = "You write replies.",
        UserMessage = Comment,
        AnswerName = name,
        AnswerDescription = "Give the reply.",
        AnswerSchemaJson = schema,
        MaxOutputTokens = 300,
    };

    private static string Quoted(string json) => JsonSerializer.Serialize(json);

    // ----- Each provider's answers, in that provider's own shape -----

    private static HttpResponseMessage Good(string provider, string answer) => ScriptedModelHandler.Json(HttpStatusCode.OK, provider switch
    {
        SocialModelProviders.Anthropic =>
            $$$"""{"id":"msg_1","type":"message","role":"assistant","model":"served-model","content":[{"type":"tool_use","id":"tu_1","name":"writeReply","input":{{{answer}}}}],"stop_reason":"tool_use","usage":{"input_tokens":11,"output_tokens":7}}""",
        SocialModelProviders.OpenAi or SocialModelProviders.Groq =>
            $$$"""{"id":"c1","object":"chat.completion","model":"served-model","choices":[{"index":0,"message":{"role":"assistant","content":{{{Quoted(answer)}}},"refusal":null},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":7}}""",
        SocialModelProviders.Gemini =>
            $$"""{"candidates":[{"content":{"role":"model","parts":[{"text":"thinking about it","thought":true},{"text":{{Quoted(answer)}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":5,"thoughtsTokenCount":2},"modelVersion":"served-model"}""",
        SocialModelProviders.Perplexity =>
            $$$"""{"id":"r1","object":"response","model":"served-model","status":"completed","output":[{"type":"message","id":"m1","role":"assistant","status":"completed","content":[{"type":"output_text","text":{{{Quoted(answer)}}}}]}],"usage":{"input_tokens":11,"output_tokens":7}}""",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    });

    /// <summary>The provider declined, or its answer was blocked or failed, with the answer's text quoting the comment.</summary>
    private static string Refused(string provider) => provider switch
    {
        SocialModelProviders.Anthropic => $$"""{"model":"m","content":[{"type":"text","text":"{{Echo}}"}],"stop_reason":"refusal"}""",
        SocialModelProviders.OpenAi or SocialModelProviders.Groq =>
            $$"""{"model":"m","choices":[{"message":{"role":"assistant","content":null,"refusal":"{{Echo}}"},"finish_reason":"stop"}]}""",
        SocialModelProviders.Gemini => $$"""{"candidates":[{"content":{"parts":[{"text":"{{Echo}}"}]},"finishReason":"SAFETY"}]}""",
        SocialModelProviders.Perplexity => $$"""{"model":"m","status":"failed","output":[{"type":"message","content":[{"type":"output_text","text":"{{Echo}}"}]}]}""",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>The answer was cut off at the output limit.</summary>
    private static string Truncated(string provider) => provider switch
    {
        SocialModelProviders.Anthropic =>
            $$$"""{"model":"m","content":[{"type":"tool_use","name":"writeReply","input":{"reply":"{{{Echo}}}"}}],"stop_reason":"max_tokens"}""",
        SocialModelProviders.OpenAi or SocialModelProviders.Groq =>
            $$"""{"model":"m","choices":[{"message":{"role":"assistant","content":"{\"reply\":\"{{Echo}}"},"finish_reason":"length"}]}""",
        SocialModelProviders.Gemini => $$"""{"candidates":[{"content":{"parts":[{"text":"{\"reply\":\"{{Echo}}"}]},"finishReason":"MAX_TOKENS"}]}""",
        SocialModelProviders.Perplexity =>
            $$"""{"model":"m","status":"incomplete","output":[{"type":"message","content":[{"type":"output_text","text":"{\"reply\":\"{{Echo}}"}]}]}""",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>A finished answer that is not the JSON object the schema asked for.</summary>
    private static string NotAnObject(string provider) => provider switch
    {
        SocialModelProviders.Anthropic => $$"""{"model":"m","content":[{"type":"text","text":"{{Echo}}"}],"stop_reason":"end_turn"}""",
        SocialModelProviders.OpenAi or SocialModelProviders.Groq =>
            $$"""{"model":"m","choices":[{"message":{"role":"assistant","content":"{{Echo}}"},"finish_reason":"stop"}]}""",
        SocialModelProviders.Gemini => $$"""{"candidates":[{"content":{"parts":[{"text":"{{Echo}}"}]},"finishReason":"STOP"}]}""",
        SocialModelProviders.Perplexity =>
            $$"""{"model":"m","status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"{{Echo}}"}]}]}""",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>An error body as the provider writes it, quoting the request back in its message.</summary>
    private static (string Body, string ErrorType) Error(string provider) => provider switch
    {
        SocialModelProviders.Anthropic => ($$$"""{"type":"error","error":{"type":"overloaded_error","message":"{{{Echo}}}"}}""", "overloaded_error"),
        SocialModelProviders.OpenAi or SocialModelProviders.Groq =>
            ($$$"""{"error":{"message":"{{{Echo}}}","type":"invalid_request_error","code":null}}""", "invalid_request_error"),
        SocialModelProviders.Gemini => ($$$"""{"error":{"code":400,"message":"{{{Echo}}}","status":"INVALID_ARGUMENT"}}""", "INVALID_ARGUMENT"),
        SocialModelProviders.Perplexity => ($$$"""{"error":{"message":"{{{Echo}}}","type":"invalid_request","code":400}}""", "invalid_request"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static void AssertOneErrorWithNothingPrivate(Built built, string? error)
    {
        var line = Assert.Single(built.Log());
        Assert.Equal(LogLevel.Error, line.Level);
        foreach (var text in new[] { line.Message, error ?? string.Empty })
        {
            Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Oak Street", text, StringComparison.Ordinal);
            Assert.DoesNotContain("You write replies", text, StringComparison.Ordinal);
        }
    }

    // ----- The contract -----

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_good_answer_returns_the_answer_who_served_it_and_its_size(string provider)
    {
        var built = Build(provider, (_, _) => Good(provider, GoodAnswer));

        var result = await built.Client.AnswerAsync(Request());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Thanks for asking.", result.Value!.Answer.GetProperty("reply").GetString());
        Assert.Equal(provider, result.Value.Provider);
        Assert.Equal("served-model", result.Value.Model);
        Assert.Equal(11, result.Value.InputTokens);
        Assert.Equal(7, result.Value.OutputTokens);
        Assert.Equal(1, built.Handler.Calls);
        Assert.Empty(built.Log());
        Assert.Equal(provider, built.Client.Provider);
        Assert.Equal("test-model", built.Client.Model);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task The_request_carries_the_key_the_model_the_prompt_the_comment_and_the_schema_unchanged(string provider)
    {
        var request = Request();
        var built = Build(provider, (_, _) => Good(provider, GoodAnswer));

        await built.Client.AnswerAsync(request);

        var sent = built.Handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, sent.Method);
        using var body = JsonDocument.Parse(built.Handler.LastBody!);
        var root = body.RootElement;
        JsonElement schema;
        switch (provider)
        {
            case SocialModelProviders.Anthropic:
                Assert.Equal("https://anthropic.test/endpoint", sent.RequestUri!.ToString());
                Assert.Equal("test-key", sent.Headers.GetValues("x-api-key").Single());
                Assert.Equal("2023-06-01", sent.Headers.GetValues("anthropic-version").Single());
                Assert.Equal("test-model", root.GetProperty("model").GetString());
                Assert.Equal(300, root.GetProperty("max_tokens").GetInt32());
                Assert.Equal("You write replies.", root.GetProperty("system").GetString());
                Assert.Equal(Comment, Assert.Single(root.GetProperty("messages").EnumerateArray()).GetProperty("content").GetString());
                Assert.Equal("tool", root.GetProperty("tool_choice").GetProperty("type").GetString());
                Assert.Equal("writeReply", root.GetProperty("tool_choice").GetProperty("name").GetString());
                schema = Assert.Single(root.GetProperty("tools").EnumerateArray()).GetProperty("input_schema");
                break;
            case SocialModelProviders.OpenAi:
            case SocialModelProviders.Groq:
                Assert.Equal($"https://{provider}.test/endpoint", sent.RequestUri!.ToString());
                Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
                Assert.Equal("test-key", sent.Headers.Authorization.Parameter);
                Assert.Equal("test-model", root.GetProperty("model").GetString());
                Assert.Equal(300, root.GetProperty("max_completion_tokens").GetInt32());
                var messages = root.GetProperty("messages").EnumerateArray().ToList();
                Assert.Equal("system", messages[0].GetProperty("role").GetString());
                Assert.Equal("You write replies.", messages[0].GetProperty("content").GetString());
                Assert.Equal("user", messages[1].GetProperty("role").GetString());
                Assert.Equal(Comment, messages[1].GetProperty("content").GetString());
                var format = root.GetProperty("response_format");
                Assert.Equal("json_schema", format.GetProperty("type").GetString());
                Assert.Equal("writeReply", format.GetProperty("json_schema").GetProperty("name").GetString());
                Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
                Assert.False(root.TryGetProperty("tools", out _));
                schema = format.GetProperty("json_schema").GetProperty("schema");
                break;
            case SocialModelProviders.Gemini:
                Assert.Equal("https://gemini.test/endpoint/test-model:generateContent", sent.RequestUri!.ToString());
                Assert.Equal("test-key", sent.Headers.GetValues("x-goog-api-key").Single());
                Assert.False(sent.RequestUri.Query.Contains("key=", StringComparison.Ordinal));
                Assert.Equal("You write replies.", Assert.Single(root.GetProperty("systemInstruction").GetProperty("parts").EnumerateArray()).GetProperty("text").GetString());
                var turn = Assert.Single(root.GetProperty("contents").EnumerateArray());
                Assert.Equal(Comment, Assert.Single(turn.GetProperty("parts").EnumerateArray()).GetProperty("text").GetString());
                var config = root.GetProperty("generationConfig");
                Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
                Assert.Equal(300, config.GetProperty("maxOutputTokens").GetInt32());
                Assert.False(root.TryGetProperty("tools", out _));
                schema = config.GetProperty("responseJsonSchema");
                break;
            case SocialModelProviders.Perplexity:
                Assert.Equal("https://perplexity.test/endpoint", sent.RequestUri!.ToString());
                Assert.Equal("test-key", sent.Headers.Authorization!.Parameter);
                Assert.Equal("test-model", root.GetProperty("model").GetString());
                Assert.Equal("You write replies.", root.GetProperty("instructions").GetString());
                Assert.Equal(Comment, root.GetProperty("input").GetString());
                Assert.Equal(300, root.GetProperty("max_output_tokens").GetInt32());
                Assert.Equal("writeReply", root.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString());
                // No search: no tools offered and no effort level, whose tools cannot be turned off.
                Assert.False(root.TryGetProperty("tools", out _));
                Assert.False(root.TryGetProperty("effort", out _));
                Assert.False(root.TryGetProperty("preset", out _));
                schema = root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }

        Assert.Equal(request.AnswerSchemaJson, schema.GetRawText());
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_error_status_is_model_failed_once_with_the_error_type_and_no_body(string provider)
    {
        var (body, errorType) = Error(provider);
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, (HttpStatusCode)529 })
        {
            var built = Build(provider, (_, _) => ScriptedModelHandler.Json(status, body));

            var result = await built.Client.AnswerAsync(Request());

            Assert.False(result.IsSuccess);
            Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
            Assert.Equal(1, built.Handler.Calls);
            AssertOneErrorWithNothingPrivate(built, result.Error);
            Assert.Contains(errorType, built.Log()[0].Message, StringComparison.Ordinal);
            Assert.Contains(((int)status).ToString(), built.Log()[0].Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_refusal_a_cut_off_answer_and_an_answer_that_is_not_an_object_each_fail_once(string provider)
    {
        foreach (var body in new[] { Refused(provider), Truncated(provider), NotAnObject(provider), "not json", "[1,2]" })
        {
            var built = Build(provider, (_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK, body));

            var result = await built.Client.AnswerAsync(Request());

            Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
            Assert.Equal(1, built.Handler.Calls);
            AssertOneErrorWithNothingPrivate(built, result.Error);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_timeout_is_model_failed_once(string provider)
    {
        var built = Build(provider, (_, _) => throw new TaskCanceledException("timed out"));

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains("did not answer within 30 seconds", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, built.Handler.Calls);
        AssertOneErrorWithNothingPrivate(built, result.Error);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_unreachable_api_is_model_failed_once(string provider)
    {
        var built = Build(provider, (_, _) => throw new HttpRequestException("no route"));

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Equal(1, built.Handler.Calls);
        AssertOneErrorWithNothingPrivate(built, result.Error);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_cancelled_call_is_cancellation_and_not_a_failure(string provider)
    {
        using var cts = new CancellationTokenSource();
        var built = Build(provider, (_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException("cancelled");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => built.Client.AnswerAsync(Request(), cts.Token));
        Assert.Empty(built.Log());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task No_api_key_is_not_configured_and_nothing_is_sent(string provider)
    {
        var built = Build(provider, (_, _) => throw new InvalidOperationException("must not send"), apiKey: "");

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Equal(0, built.Handler.Calls);
        AssertOneErrorWithNothingPrivate(built, result.Error);
        var setting = $"LlmProviders__{SocialModelProviders.SettingsSection(provider)}__ApiKey";
        Assert.DoesNotContain("LlmProviders", result.Error, StringComparison.Ordinal);
        Assert.Contains(setting, built.Log()[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task No_model_is_not_configured_and_nothing_is_sent(string provider)
    {
        var built = Build(provider, (_, _) => throw new InvalidOperationException("must not send"), model: " ");

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Equal(0, built.Handler.Calls);
        AssertOneErrorWithNothingPrivate(built, result.Error);
        Assert.Contains($"LlmProviders__{SocialModelProviders.SettingsSection(provider)}__Model", built.Log()[0].Message, StringComparison.Ordinal);
        Assert.Null(built.Client.Model);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_schema_that_is_not_an_object_or_a_name_not_every_provider_takes_is_refused_before_any_call(string provider)
    {
        foreach (var request in new[] { Request("not json"), Request("[1,2]"), Request(name: "write_reply"), Request(name: "") })
        {
            var built = Build(provider, (_, _) => throw new InvalidOperationException("must not send"));

            var result = await built.Client.AnswerAsync(request);

            Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
            Assert.Equal(0, built.Handler.Calls);
            AssertOneErrorWithNothingPrivate(built, result.Error);
        }
    }

    // ----- What one provider does that the others do not -----

    [Fact]
    public async Task Anthropic_fails_when_it_answers_with_another_tool()
    {
        var built = Build(SocialModelProviders.Anthropic, (_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK,
            """{"model":"m","content":[{"type":"tool_use","name":"anotherTool","input":{"reply":"x"}}],"stop_reason":"tool_use"}"""));

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains("did not answer with the writeReply tool", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anthropic_reads_its_key_from_ANTHROPIC_API_KEY_when_the_setting_is_empty()
    {
        var values = Settings(SocialModelProviders.Anthropic, apiKey: "");
        values[SocialModelSettings.AnthropicKeyVariable] = "env-key";
        var settings = SocialModelSettings.From(name => values.GetValueOrDefault(name));
        var handler = new ScriptedModelHandler((_, _) => Good(SocialModelProviders.Anthropic, GoodAnswer));
        var client = new AnthropicSocialModelClient(
            new HttpClient(handler), settings, new LlmConcurrencyGate(1), TimeProvider.System, new RecordingLog<AnthropicSocialModelClient>());

        var result = await client.AnswerAsync(Request());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("env-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task Gemini_fails_when_the_prompt_is_blocked()
    {
        var built = Build(SocialModelProviders.Gemini, (_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK,
            """{"promptFeedback":{"blockReason":"PROHIBITED_CONTENT"}}"""));

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains("PROHIBITED_CONTENT", result.Error, StringComparison.Ordinal);
    }

    public static TheoryData<string> PerplexityOutputsThatSearched => new()
    {
        """{"type":"search_results","results":[{"url":"https://example.test","title":"PRIVATE-ECHO-IN-THE-ANSWER"}]}""",
        """{"type":"fetch_url_results","contents":[]}""",
        """{"type":"function_call","name":"lookup","arguments":"{}"}""",
    };

    [Theory]
    [MemberData(nameof(PerplexityOutputsThatSearched))]
    public async Task Perplexity_fails_and_uses_nothing_when_its_output_holds_more_than_a_message(string item)
    {
        var built = Build(SocialModelProviders.Perplexity, (_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK,
            $$"""{"model":"m","status":"completed","output":[{{item}},{"type":"message","content":[{"type":"output_text","text":{{Quoted(GoodAnswer)}}}]}]}"""));

        var result = await built.Client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains("no tool was offered", result.Error, StringComparison.Ordinal);
        AssertOneErrorWithNothingPrivate(built, result.Error);
    }

    [Fact]
    public async Task With_no_provider_set_every_call_fails_not_configured_and_names_no_setting()
    {
        var log = new RecordingLog<UnconfiguredSocialModelClient>();
        var client = new UnconfiguredSocialModelClient(log);

        var result = await client.AnswerAsync(Request());

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.DoesNotContain("LlmProviders", result.Error, StringComparison.Ordinal);
        var line = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(SocialModelSettings.ProviderVariable, line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", line.Message, StringComparison.Ordinal);
        Assert.Null(client.Provider);
        Assert.Null(client.Model);
    }

    [Fact]
    public void The_request_type_prints_only_its_purpose()
    {
        Assert.Equal("SocialModelRequest(draft a reply)", Request().ToString());
    }
}
