using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public sealed record GccV2AgentTestEvent(
    string ContractVersion, Guid TestRunId, Guid AgentVersionId, string VersionDigest,
    string Status, int ProgressPercent, string Phase, string? Message, int AttemptCount,
    DateTimeOffset QueuedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    string? ResultJson, string? Error);

public sealed class GccV2AgentTestProgressNotifier(IHubContext<GccV2RealtimeHub> hub)
{
    public Task PushAsync(GccV2AgentTestRunDto run, string? message = null, CancellationToken ct = default) =>
        hub.Clients.Group(GccV2RealtimeHub.AgentTestGroup(run.Id)).SendAsync(
            "AgentTestEvent", ToEvent(run, message), ct);

    public static GccV2AgentTestEvent ToEvent(GccV2AgentTestRunDto run, string? message = null) => new(
        "gcc-agent-test-event.v1", run.Id, run.AgentVersionId, run.VersionDigest,
        run.Status, run.ProgressPercent, run.Phase, message, run.AttemptCount,
        run.QueuedAtUtc, run.StartedAtUtc, run.CompletedAtUtc, run.ResultJson, run.Error);
}
