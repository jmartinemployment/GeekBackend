using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>Persists job transitions atomically, then pushes events to connected hub clients.</summary>
public sealed class GccV2JobEventWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ProgressNotifier _notifier;
    private readonly ILogger<GccV2JobEventWriter> _logger;

    public GccV2JobEventWriter(
        HttpGccV2Repository repo,
        GccV2ProgressNotifier notifier,
        ILogger<GccV2JobEventWriter> logger)
    {
        _repo = repo;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<GccV2JobEventDto> AppendAsync(
        Guid jobId,
        Guid ownerUserId,
        string type,
        object payload,
        bool wake = false,
        CancellationToken ct = default)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOpts);
        var evt = await _repo.AppendJobEventAsync(jobId, new AppendGccV2JobEventCommand(type, payloadJson, wake), ct);
        await TryPushAsync(jobId, ownerUserId, evt, ct);
        return evt;
    }

    /// <summary>Persist job patch + event in one repository transaction, then hub-push the event.</summary>
    public async Task<GccV2JobTransitionResultDto> TransitionAsync(
        Guid jobId,
        Guid ownerUserId,
        ApplyGccV2JobTransitionCommand command,
        CancellationToken ct = default)
    {
        var result = await _repo.ApplyJobTransitionAsync(jobId, command, ct);
        if (result.Event is not null)
            await TryPushAsync(jobId, ownerUserId, result.Event, ct);
        return result;
    }

    public Task<GccV2JobTransitionResultDto> FailAsync(
        Guid jobId,
        Guid ownerUserId,
        string error,
        CancellationToken ct = default) =>
        TransitionAsync(
            jobId,
            ownerUserId,
            new ApplyGccV2JobTransitionCommand(
                Status: "failed",
                Error: error,
                ReleaseClaim: true,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                EventType: "JobFailed",
                EventPayloadJson: JsonSerializer.Serialize(new { error }, JsonOpts)),
            ct);

    private async Task TryPushAsync(
        Guid jobId,
        Guid ownerUserId,
        GccV2JobEventDto evt,
        CancellationToken ct)
    {
        try
        {
            await _notifier.PushAsync(jobId, ownerUserId, evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Realtime hub push failed for job {JobId} event {EventType}; persistence succeeded.",
                jobId,
                evt.Type);
        }
    }
}
