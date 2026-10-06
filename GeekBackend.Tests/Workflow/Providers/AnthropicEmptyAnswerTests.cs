using System.Net;
using System.Text;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.Workflow.Providers;

/// <summary>
/// A 200 from Anthropic with no block of the kind the call needs is a failure that says what came back.
/// </summary>
/// <remarks>
/// On 2026-10-06 every piece of a Generate failed with "Anthropic response contained no text content
/// block" after a 200 that took 25 seconds, and that sentence was all the log held. The body was read
/// and thrown away, so whether the model refused, ran out of output tokens or answered in a block type
/// this code does not read could not be told after the fact.
/// </remarks>
public sealed class AnthropicEmptyAnswerTests
{
    [Fact]
    public async Task A_refusal_with_no_content_names_the_stop_reason_and_logs_the_body()
    {
        var log = new CapturingLogger();
        var provider = Provider(
            """{"id":"msg_1","type":"message","role":"assistant","model":"m","content":[],"stop_reason":"refusal","usage":{"input_tokens":10,"output_tokens":0}}""",
            log);

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Equal("Anthropic response contained no text content block (stop_reason: refusal; no content blocks).", ex.Message);
        var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("stop_reason refusal", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"stop_reason\":\"refusal\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_in_blocks_this_code_does_not_read_names_them()
    {
        var provider = Provider(
            """{"id":"msg_2","type":"message","role":"assistant","model":"m","content":[{"type":"thinking","thinking":"..."},{"type":"redacted_thinking","data":"x"}],"stop_reason":"max_tokens"}""",
            new CapturingLogger());

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Equal(
            "Anthropic response contained no text content block (stop_reason: max_tokens; 2 content block(s): thinking, redacted_thinking).",
            ex.Message);
    }

    [Fact]
    public async Task A_forced_tool_answer_without_its_tool_block_says_so_the_same_way()
    {
        var provider = Provider(
            """{"id":"msg_3","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"I cannot."}],"stop_reason":"end_turn"}""",
            new CapturingLogger());

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(
            Request() with { JsonSchemaName = "shape", JsonSchema = """{"type":"object"}""" }));

        Assert.Equal("Anthropic response contained no tool_use content block (stop_reason: end_turn; 1 content block(s): text).", ex.Message);
    }

    [Fact]
    public async Task A_text_answer_is_returned_as_before()
    {
        var provider = Provider(
            """{"id":"msg_4","type":"message","role":"assistant","model":"m-used","content":[{"type":"text","text":"Hello."}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":2}}""",
            new CapturingLogger());

        var result = await provider.CompleteAsync(Request());

        Assert.Equal("Hello.", result.Content);
        Assert.Equal("m-used", result.ModelUsed);
        Assert.Equal((3, 2), (result.PromptTokens, result.CompletionTokens));
    }

    private static ChatCompletionRequest Request() =>
        new([new ChatMessage(ChatRole.System, "You write."), new ChatMessage(ChatRole.User, "Write.")]);

    private static AnthropicProvider Provider(string responseBody, ILogger<AnthropicProvider> logger)
    {
        var handler = new StubHandler(responseBody);
        var options = Options.Create(new LlmProvidersOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "test-key", Model = "m", BaseUrl = "https://anthropic.test/v1/messages" },
        });
        return new AnthropicProvider(new HttpClient(handler), options, logger);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CapturingLogger : ILogger<AnthropicProvider>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
