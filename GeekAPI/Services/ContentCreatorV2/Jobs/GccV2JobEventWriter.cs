using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>Persists a job event then pushes it to any connected hub clients.</summary>
public sealed class GccV2JobEventWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ProgressNotifier _notifier;

    public GccV2JobEventWriter(HttpGccV2Repository repo, GccV2ProgressNotifier notifier)
    {
        _repo = repo;
        _notifier = notifier;
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
        await _notifier.PushAsync(jobId, ownerUserId, evt, ct);
        return evt;
    }
}
