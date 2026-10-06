using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A Generate run's record: every call, verdict and outcome written the moment it happens, filed under
/// the piece being written, and a record that cannot be written counted rather than hidden (Jeff,
/// 2026-10-06: "implement detailed comprehensive logging to properly diagnose errors").
/// </summary>
public sealed class GccRunLogTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Events_are_written_as_they_happen_filed_under_the_piece_in_scope()
    {
        var written = new List<GccGenerateJobEventWrite>();
        var jobId = Guid.NewGuid();
        var log = GccRunLog.Begin(jobId, (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        await log.RecordAsync("started", new { provider = "OpenAi" });
        using (GccRunLog.ForPiece("pillar"))
        {
            await GccRunLog.RecordIfAnyAsync("verdict", new { clean = false });
            using (GccRunLog.ForPiece("tool: Ramp"))
                await GccRunLog.RecordIfAnyAsync("call", new { model = "m" });
            await GccRunLog.RecordIfAnyAsync("outcome", new { type = "pillar" }, piece: "pillar");
        }
        await log.RecordAsync("completed", new { ok = true });

        Assert.Equal(jobId, log.JobId);
        Assert.Equal(["started", "verdict", "call", "outcome", "completed"], written.Select(w => w.Kind));
        Assert.Equal([null, "pillar", "tool: Ramp", "pillar", null], written.Select(w => w.Piece));
        Assert.Equal("""{"provider":"OpenAi"}""", written[0].PayloadJson);
        Assert.Equal(5, log.Events);
        Assert.Equal(0, log.RecordingFailures);
        Assert.Null(GccRunLog.Piece);
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_is_counted_and_never_fails_the_run()
    {
        var log = GccRunLog.Begin(Guid.NewGuid(), (_, _) => throw new HttpRequestException("repository down"), NullLogger.Instance);

        await log.RecordAsync("started", new { provider = "OpenAi" });
        await log.RecordAsync("call", new { model = "m" });

        Assert.Equal(2, log.RecordingFailures);
        Assert.Equal(0, log.Events);
    }

    [Fact]
    public async Task The_recording_provider_writes_what_was_sent_what_came_back_the_model_and_the_tokens_and_a_failure_as_one()
    {
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);
        var request = new ChatCompletionRequest(
            [new ChatMessage(ChatRole.System, "You write."), new ChatMessage(ChatRole.User, "Write the pillar.")],
            JsonSchemaName: "sections");

        var ok = new GccRecordingProvider(new Answering(new ChatCompletionResult("the body", "gpt-x", 1200, 300)));
        var failing = new GccRecordingProvider(new Answering(null));
        using (GccRunLog.ForPiece("pillar"))
        {
            var result = await ok.CompleteAsync(request);
            Assert.Equal("the body", result.Content);
            await Assert.ThrowsAsync<ContentGenerationException>(() => failing.CompleteAsync(request));
        }

        Assert.Equal(["call", "call"], written.Select(w => w.Kind));
        Assert.All(written, w => Assert.Equal("pillar", w.Piece));
        using var first = JsonDocument.Parse(written[0].PayloadJson);
        Assert.Equal("gpt-x", first.RootElement.GetProperty("model").GetString());
        Assert.Equal("sections", first.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1200, first.RootElement.GetProperty("promptTokens").GetInt32());
        Assert.Equal(300, first.RootElement.GetProperty("completionTokens").GetInt32());
        Assert.Equal("You write.", first.RootElement.GetProperty("system").GetString());
        Assert.Equal("Write the pillar.", first.RootElement.GetProperty("user").GetString());
        Assert.Equal("the body", first.RootElement.GetProperty("response").GetString());
        using var second = JsonDocument.Parse(written[1].PayloadJson);
        Assert.Contains("ContentGenerationException", second.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(second.RootElement.TryGetProperty("response", out _));
    }

    [Fact]
    public async Task Without_a_run_nothing_is_recorded_and_nothing_fails()
    {
        // A fresh async flow: no Begin has happened on it.
        await Task.Run(async () =>
        {
            Assert.Null(GccRunLog.Current);
            await GccRunLog.RecordIfAnyAsync("call", new { model = "m" });
            var provider = new GccRecordingProvider(new Answering(new ChatCompletionResult("x", "m", null, null)));
            var result = await provider.CompleteAsync(new ChatCompletionRequest([new ChatMessage(ChatRole.User, "u")]));
            Assert.Equal("x", result.Content);
        });
    }

    private sealed class Answering(ChatCompletionResult? result) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            result is null
                ? throw new ContentGenerationException("provider said no")
                : Task.FromResult(result);
    }
}
