using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

public sealed record GccV2TaskAgentRunEvent(
    string ContractVersion,
    string Kind,
    Guid RunId,
    int Seq,
    string Status,
    string Phase,
    int ProgressPercent,
    string? TerminalError,
    object? SharedContext,
    string? Message);

public sealed class GccV2TaskAgentRunProgressNotifier(IHubContext<GccV2RealtimeHub> hub)
{
    public Task PushAsync(
        GccV2TaskRunDto run, string kind, int seq, string? message = null, CancellationToken ct = default) =>
        hub.Clients.Group(GccV2RealtimeHub.TaskAgentRunGroup(run.Id)).SendAsync(
            "TaskAgentRunEvent", ToEvent(run, kind, seq, message), ct);

    public static GccV2TaskAgentRunEvent ToEvent(
        GccV2TaskRunDto run, string kind, int seq, string? message = null) => new(
        "gcc-task-agent-run-event.v1",
        kind,
        run.Id,
        seq,
        run.Status,
        run.Phase,
        run.ProgressPercent,
        run.TerminalError,
        run.ContextManifestId is null && string.IsNullOrWhiteSpace(run.ContextManifestDigest)
            ? null
            : new { contextManifestId = run.ContextManifestId, contextManifestDigest = run.ContextManifestDigest },
        message);

    public static int LatestSeq(GccV2TaskRunDto run) =>
        run.Events is { Count: > 0 } events ? events.Max(x => x.Seq) : 1;
}
