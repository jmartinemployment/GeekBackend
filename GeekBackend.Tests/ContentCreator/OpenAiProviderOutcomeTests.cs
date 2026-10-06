using System.Net;
using System.Text;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A call the model cut short, refused, had filtered or answered with nothing is a failed call, named
/// as one the first time. These drove "did not return a valid sections array" with the first 200
/// characters, and an <c>ArgumentNullException</c> from a regex, before: the reason was in the response
/// the whole time and nothing read it.
/// </summary>
public sealed class OpenAiProviderOutcomeTests
{
    private static OpenAiProvider Provider(HttpMessageHandler handler, int timeoutSeconds = 120)
    {
        var options = Options.Create(new LlmProvidersOptions
        {
            OpenAi = { ApiKey = "k", Model = "gpt-x", BaseUrl = "http://localhost/v1/chat/completions", TimeoutSeconds = timeoutSeconds },
        });
        return new OpenAiProvider(new HttpClient(handler), options, NullLogger<OpenAiProvider>.Instance);
    }

    private static ChatCompletionRequest Request() =>
        new([new ChatMessage(ChatRole.User, "write")], MaxOutputTokens: 16384);

    private static Func<HttpRequestMessage, HttpResponseMessage> Reply(string choiceJson) => _ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"model\":\"gpt-x\",\"choices\":[{" + choiceJson.Trim() + "}],"
                + "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"prompt_tokens_details\":{\"cached_tokens\":8}}}",
                Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task A_finished_answer_returns_its_text_its_finish_reason_and_its_cached_tokens()
    {
        var provider = Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":"{\"sections\":[]}"},"finish_reason":"stop"
            """)));

        var result = await provider.CompleteAsync(Request());

        Assert.Equal("""{"sections":[]}""", result.Content);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(8, result.CachedTokens);
    }

    [Fact]
    public async Task An_answer_cut_off_at_the_output_limit_fails_by_name_and_carries_what_was_written()
    {
        var provider = Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":"{\"sections\":[{\"tag\":\"h2\""},"finish_reason":"length"
            """)));

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Contains("output limit of 16384 tokens", ex.Message, StringComparison.Ordinal);
        Assert.Equal("length", ex.FinishReason);
        Assert.Equal("""{"sections":[{"tag":"h2" """.TrimEnd(), ex.PartialResponse);
    }

    [Fact]
    public async Task A_refusal_fails_with_the_models_own_words()
    {
        var provider = Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":null,"refusal":"I can't help with that."},"finish_reason":"stop"
            """)));

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Contains("refused the request: I can't help with that.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_filtered_answer_fails_by_name()
    {
        var provider = Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":""},"finish_reason":"content_filter"
            """)));

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Contains("content_filter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_with_no_content_fails_naming_why_rather_than_throwing_from_a_regex()
    {
        var provider = Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":null},"finish_reason":"stop"
            """)));

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Contains("returned no content (finish_reason stop)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_the_http_client_abandons_is_a_timeout_naming_the_seconds()
    {
        var provider = Provider(new Stub(_ => throw new TaskCanceledException("timed out")), timeoutSeconds: 90);

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));

        Assert.Contains("did not answer within 90 seconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_runs_own_cancellation_stays_a_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var provider = Provider(new Stub(_ => throw new TaskCanceledException("cancelled")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task The_run_log_records_the_finish_reason_the_cached_tokens_and_a_truncated_responses_text()
    {
        var written = new List<GeekApplication.Models.ContentCreator.GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        var ok = new GccRecordingProvider(Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":"body"},"finish_reason":"stop"
            """))));
        var cut = new GccRecordingProvider(Provider(new Stub(Reply("""
            "message":{"role":"assistant","content":"half a bo"},"finish_reason":"length"
            """))));

        await ok.CompleteAsync(Request());
        await Assert.ThrowsAsync<ContentGenerationException>(() => cut.CompleteAsync(Request()));

        using var first = System.Text.Json.JsonDocument.Parse(written[0].PayloadJson);
        Assert.Equal("stop", first.RootElement.GetProperty("finishReason").GetString());
        Assert.Equal(8, first.RootElement.GetProperty("cachedTokens").GetInt32());
        using var second = System.Text.Json.JsonDocument.Parse(written[1].PayloadJson);
        Assert.Equal("length", second.RootElement.GetProperty("finishReason").GetString());
        Assert.Equal("half a bo", second.RootElement.GetProperty("response").GetString());
        Assert.Contains("output limit", second.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
