using System.Diagnostics;
using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// A Generate run's record, written as the run goes: what it was grounded on, every model call with
/// what was sent and what came back, every guard verdict with the draft it judged, every batch
/// shortfall, every piece's outcome, and how the run ended.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-06, after three refused runs ($14 for no page) with nothing to read but one log line:
/// "implement detailed comprehensive logging to properly diagnose errors". The per-version evidence
/// design records only what was saved; a refused run saves nothing, so it recorded nothing. This
/// records the run itself, refused or not, one event at a time to <c>gcc_generate_job_events</c> the
/// moment each thing happens -- so a run a redeploy cuts off still has its record up to the cut.
/// </para>
/// <para>
/// Ambient, not threaded: a run is a fan-out of types, each a lede, several body batches, a FAQ,
/// image prompts, across helpers that each resolve their own provider. <see cref="Begin"/> scopes the
/// log to the calling flow and everything it awaits; <see cref="ForPiece"/> names the piece the flow
/// is writing, so every call and verdict is filed under "pillar" or "tool: Ramp".
/// </para>
/// <para>
/// A record that cannot be written is logged at Error and counted; it never fails the run, and the
/// count is written into the run's last event so the gap is visible rather than silent.
/// </para>
/// </remarks>
public sealed class GccRunLog
{
    private static readonly AsyncLocal<GccRunLog?> CurrentLog = new();
    private static readonly AsyncLocal<string?> CurrentPiece = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly Func<IReadOnlyList<GccGenerateJobEventWrite>, CancellationToken, Task> _sink;
    private readonly ILogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _recordingFailures;
    private int _events;

    private GccRunLog(Guid jobId, Func<IReadOnlyList<GccGenerateJobEventWrite>, CancellationToken, Task> sink, ILogger logger)
    {
        JobId = jobId;
        _sink = sink;
        _logger = logger;
    }

    public Guid JobId { get; }

    /// <summary>The log the calling flow is writing into, if a run was begun.</summary>
    public static GccRunLog? Current => CurrentLog.Value;

    /// <summary>The piece the calling flow is writing, if one was named.</summary>
    public static string? Piece => CurrentPiece.Value;

    /// <summary>How many events the run has recorded so far.</summary>
    public int Events => _events;

    /// <summary>How many records could not be written. Zero is the only honest record.</summary>
    public int RecordingFailures => _recordingFailures;

    /// <summary>Milliseconds since the run's log began.</summary>
    public long ElapsedMs => _clock.ElapsedMilliseconds;

    /// <summary>A fresh log for the calling flow and everything it awaits from here.</summary>
    public static GccRunLog Begin(
        Guid jobId, Func<IReadOnlyList<GccGenerateJobEventWrite>, CancellationToken, Task> sink, ILogger logger)
    {
        var log = new GccRunLog(jobId, sink, logger);
        CurrentLog.Value = log;
        return log;
    }

    /// <summary>Names the piece the calling flow writes from here until the scope is disposed.</summary>
    public static IDisposable ForPiece(string piece)
    {
        var previous = CurrentPiece.Value;
        CurrentPiece.Value = piece;
        return new PieceScope(previous);
    }

    /// <summary>Record one event. The piece is the current one unless given.</summary>
    public async Task RecordAsync(string kind, object payload, string? piece = null, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, Json);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _recordingFailures);
            _logger.LogError(ex, "Run {JobId}: a {Kind} event could not be serialized", JobId, kind);
            return;
        }

        var write = new GccGenerateJobEventWrite(kind, piece ?? CurrentPiece.Value, json);
        try
        {
            await _sink([write], ct);
            Interlocked.Increment(ref _events);
            _logger.LogInformation(
                "Run {JobId} recorded {Kind} for {Piece} ({Bytes} bytes)",
                JobId, kind, write.Piece ?? "the run", json.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _recordingFailures);
            _logger.LogError(ex, "Run {JobId}: a {Kind} event for {Piece} could not be written", JobId, kind, write.Piece ?? "the run");
        }
    }

    /// <summary>Record if a run is being logged; nothing otherwise. For code that runs on and off a run.</summary>
    public static Task RecordIfAnyAsync(string kind, object payload, string? piece = null) =>
        Current?.RecordAsync(kind, payload, piece) ?? Task.CompletedTask;

    private sealed class PieceScope(string? previous) : IDisposable
    {
        public void Dispose() => CurrentPiece.Value = previous;
    }
}

/// <summary>
/// A provider that records every call it makes into the current run's log: what was sent, what came
/// back, which model answered, how many tokens, how long -- and a failure as a failure.
/// </summary>
public sealed class GccRecordingProvider(IContentGenerationProvider inner) : IContentGenerationProvider
{
    public LlmProviderType ProviderType => inner.ProviderType;

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        try
        {
            var result = await inner.CompleteAsync(request, cancellationToken);
            await GccRunLog.RecordIfAnyAsync("call", new
            {
                provider = ProviderType.ToString(),
                model = result.ModelUsed,
                schema = request.JsonSchemaName,
                taskClass = request.TaskClass.ToString(),
                maxOutputTokens = request.MaxOutputTokens,
                promptTokens = result.PromptTokens,
                completionTokens = result.CompletionTokens,
                cachedTokens = result.CachedTokens,
                finishReason = result.FinishReason,
                durationMs = started.ElapsedMilliseconds,
                system = Join(request, ChatRole.System),
                user = Join(request, ChatRole.User),
                response = result.Content,
            });
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await GccRunLog.RecordIfAnyAsync("call", new
            {
                provider = ProviderType.ToString(),
                model = request.Model,
                schema = request.JsonSchemaName,
                taskClass = request.TaskClass.ToString(),
                durationMs = started.ElapsedMilliseconds,
                system = Join(request, ChatRole.System),
                user = Join(request, ChatRole.User),
                error = $"{ex.GetType().Name}: {ex.Message}",
                // What the model had written when the call was judged a failure, and why it stopped:
                // a body cut off at its output ceiling is only legible with its own text beside it.
                finishReason = (ex as ContentGenerationException)?.FinishReason,
                response = (ex as ContentGenerationException)?.PartialResponse,
            });
            throw;
        }
    }

    private static string Join(ChatCompletionRequest r, ChatRole role) =>
        string.Join("\n\n", r.Messages.Where(m => m.Role == role).Select(m => m.Content));
}
