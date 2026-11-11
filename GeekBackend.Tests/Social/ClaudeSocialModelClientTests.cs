using System.Net;
using System.Text.Json;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.Social;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The default model client: one forced-tool call to the Anthropic API, read back as the tool's input.
/// Every failure is a failed Result and exactly one Error log line, the call is made once and never
/// again, and nothing from the request or the answer (a comment, a draft) reaches a log line or an
/// error message.
/// </summary>
public class ClaudeSocialModelClientTests
{
    private const string Comment = "PRIVATE-COMMENT-TEXT from Jane at 12 Oak Street";
    private const string Echo = "PRIVATE-ECHO-IN-THE-ANSWER";

    private static (ClaudeSocialModelClient Client, ScriptedModelHandler Handler, RecordingLog<ClaudeSocialModelClient> Log) Build(
        Func<HttpRequestMessage, string?, HttpResponseMessage> answer,
        string apiKey = "test-key",
        string model = "test-model",
        Func<string, string?>? environment = null)
    {
        var handler = new ScriptedModelHandler(answer);
        var options = Options.Create(new LlmProvidersOptions
        {
            Anthropic = new AnthropicOptions
            {
                ApiKey = apiKey,
                Model = model,
                BaseUrl = "https://anthropic.test/v1/messages",
                TimeoutSeconds = 30,
            },
        });
        var log = new RecordingLog<ClaudeSocialModelClient>();
        var client = new ClaudeSocialModelClient(
            new HttpClient(handler), options, new LlmConcurrencyGate(2), TimeProvider.System, log, environment ?? (_ => null));
        return (client, handler, log);
    }

    private static SocialModelRequest Request(string schema = """{"type":"object","properties":{"reply":{"type":"string"}},"required":["reply"]}""") => new()
    {
        Purpose = "draft a reply",
        SystemPrompt = "You write replies.",
        UserMessage = Comment,
        ToolName = "write_reply",
        ToolDescription = "Give the reply.",
        InputSchemaJson = schema,
        MaxOutputTokens = 300,
    };

    private static HttpResponseMessage ToolAnswer(string toolName, string input, string stopReason = "tool_use") =>
        ScriptedModelHandler.Json(HttpStatusCode.OK,
            $$"""{"id":"msg_1","type":"message","role":"assistant","model":"served-model","content":[{"type":"tool_use","id":"tu_1","name":"{{toolName}}","input":{{input}}}],"stop_reason":"{{stopReason}}"}""");

    private static void AssertOneErrorWithNothingPrivate(RecordingLog<ClaudeSocialModelClient> log, string? error)
    {
        var line = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        foreach (var text in new[] { line.Message, error ?? string.Empty })
        {
            Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Oak Street", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_good_answer_returns_the_tool_input_and_sends_a_forced_tool_call()
    {
        var (client, handler, log) = Build((_, _) => ToolAnswer("write_reply", """{"reply":"Thanks for asking."}"""));

        var result = await client.CallToolAsync(Request());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Thanks for asking.", result.Value.GetProperty("reply").GetString());
        Assert.Equal(1, handler.Calls);
        Assert.Empty(log.Entries);

        Assert.Equal("test-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest.Headers.GetValues("anthropic-version").Single());
        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(300, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("You write replies.", root.GetProperty("system").GetString());
        Assert.Equal("tool", root.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("write_reply", root.GetProperty("tool_choice").GetProperty("name").GetString());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("write_reply", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("temperature", out _));
        var turn = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", turn.GetProperty("role").GetString());
        Assert.Equal(Comment, turn.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "api_error")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error")]
    [InlineData((HttpStatusCode)529, "overloaded_error")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error")]
    public async Task An_error_status_is_model_failed_once_with_the_error_type_and_no_body(HttpStatusCode status, string errorType)
    {
        var (client, handler, log) = Build((_, _) => ScriptedModelHandler.Json(status,
            $$$"""{"type":"error","error":{"type":"{{{errorType}}}","message":"{{{Echo}}}"}}"""));

        var result = await client.CallToolAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Equal(1, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
        Assert.Contains(errorType, log.Entries[0].Message, StringComparison.Ordinal);
        Assert.Contains(((int)status).ToString(), log.Entries[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_is_model_failed_once()
    {
        var (client, handler, log) = Build((_, _) => throw new TaskCanceledException("timed out"));

        var result = await client.CallToolAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains("did not answer within 30 seconds", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task An_unreachable_api_is_model_failed_once()
    {
        var (client, handler, log) = Build((_, _) => throw new HttpRequestException("no route"));

        var result = await client.CallToolAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Equal(1, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task A_cancelled_call_is_cancellation_and_not_a_failure()
    {
        using var cts = new CancellationTokenSource();
        var (client, _, log) = Build((_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException("cancelled");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CallToolAsync(Request(), cts.Token));
        Assert.Empty(log.Entries);
    }

    public static TheoryData<string, string> AnswersWithNoUsableTool => new()
    {
        { "not json", "did not parse" },
        { $$"""{"model":"m","content":[{"type":"text","text":"{{Echo}}"}],"stop_reason":"end_turn"}""", "did not answer with the write_reply tool" },
        { """{"model":"m","content":[{"type":"tool_use","name":"another_tool","input":{"reply":"x"}}],"stop_reason":"tool_use"}""", "did not answer with the write_reply tool" },
        { """{"model":"m","content":[{"type":"tool_use","name":"write_reply","input":"x"}],"stop_reason":"tool_use"}""", "did not answer with the write_reply tool" },
        { """{"model":"m","content":[],"stop_reason":"refusal"}""", "refusal" },
        { $$$"""{"model":"m","content":[{"type":"tool_use","name":"write_reply","input":{"reply":"{{{Echo}}}"}}],"stop_reason":"max_tokens"}""", "max_tokens" },
    };

    [Theory]
    [MemberData(nameof(AnswersWithNoUsableTool))]
    public async Task A_200_without_a_usable_tool_answer_is_model_failed_and_says_why(string body, string reason)
    {
        var (client, handler, log) = Build((_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK, body));

        var result = await client.CallToolAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task No_api_key_is_not_configured_and_nothing_is_sent()
    {
        var (client, handler, log) = Build((_, _) => throw new InvalidOperationException("must not send"), apiKey: "");

        var result = await client.CallToolAsync(Request());

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Contains(ClaudeSocialModelClient.ApiKeyVariable, result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task The_key_is_read_from_the_environment_when_the_setting_is_empty()
    {
        var (client, handler, _) = Build(
            (_, _) => ToolAnswer("write_reply", """{"reply":"Hello."}"""),
            apiKey: "",
            environment: name => name == ClaudeSocialModelClient.ApiKeyVariable ? "env-key" : null);

        var result = await client.CallToolAsync(Request());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("env-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task No_model_is_not_configured_and_nothing_is_sent()
    {
        var (client, handler, log) = Build((_, _) => throw new InvalidOperationException("must not send"), model: " ");

        var result = await client.CallToolAsync(Request());

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Contains(ClaudeSocialModelClient.ModelSetting, result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task A_schema_that_is_not_an_object_is_refused_before_any_call(string schema)
    {
        var (client, handler, log) = Build((_, _) => throw new InvalidOperationException("must not send"));

        var result = await client.CallToolAsync(Request(schema));

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(0, handler.Calls);
        AssertOneErrorWithNothingPrivate(log, result.Error);
    }

    [Fact]
    public void The_request_type_prints_only_its_purpose()
    {
        Assert.Equal("SocialModelRequest(draft a reply)", Request().ToString());
    }
}
