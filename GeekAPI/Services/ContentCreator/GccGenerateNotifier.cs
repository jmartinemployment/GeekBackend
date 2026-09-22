using GeekAPI.Controllers.Workflow.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Pushes Content Creator generate progress to the v1 realtime hub. Mirrors
/// <see cref="GeekAPI.Services.Workflow.Services.ToolsJobProgressNotifier"/>, which is the
/// established pattern for this hub.
/// </summary>
public sealed class GccGenerateNotifier
{
    private readonly IHubContext<WorkflowRealtimeHub> _hub;

    public GccGenerateNotifier(IHubContext<WorkflowRealtimeHub> hub) => _hub = hub;

    public Task PushJobAsync(GccJob job, CancellationToken ct = default) =>
        _hub.Clients.Group(WorkflowRealtimeHub.GccGenerateGroup(job.Id))
            .SendAsync("GccGenerateEvent", GccGenerateEventMapper.Map(job), ct);

    /// <summary>
    /// One event per requested content type, sent the moment that type finishes -- the UI fills in
    /// progressively instead of waiting on the slowest one, and a partial outcome stays legible:
    /// four artifacts plus one named refusal rather than a single opaque failure.
    /// </summary>
    public Task PushTypeAsync(
        Guid jobId, string contentType, object? artifact, string? error, CancellationToken ct = default) =>
        _hub.Clients.Group(WorkflowRealtimeHub.GccGenerateGroup(jobId))
            .SendAsync(
                "GccGenerateTypeEvent",
                new
                {
                    jobId,
                    contentType,
                    status = error is null ? "ready" : "failed",
                    artifact,
                    error,
                },
                ct);
}

internal static class GccGenerateEventMapper
{
    public static object Map(GccJob job) =>
        new
        {
            jobId = job.Id,
            createId = job.CreateId,
            kind = job.Kind,
            status = job.Status,
            error = job.Error,
            resultJson = job.ResultJson,
            completedAtUtc = job.CompletedAtUtc,
        };
}
