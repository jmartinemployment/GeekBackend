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

    /// <summary>
    /// A piece that was saved with a gap the operator should see. Status "warning": the piece exists
    /// (it is not "failed") and this is not its outcome (that was pushed as "ready" with the artifact).
    /// </summary>
    public Task PushWarningAsync(Guid jobId, string contentType, string warning, CancellationToken ct = default) =>
        _hub.Clients.Group(WorkflowRealtimeHub.GccGenerateGroup(jobId))
            .SendAsync(
                "GccGenerateTypeEvent",
                new
                {
                    jobId,
                    contentType,
                    status = "warning",
                    artifact = (object?)null,
                    error = warning,
                },
                ct);

    /// <summary>
    /// The tool pre-flight, pushed before any tool page is drafted: one row per declared partner saying
    /// whether it can be grounded and why.
    /// </summary>
    /// <remarks>
    /// Its own event rather than a <c>GccGenerateTypeEvent</c>, because that event's status is
    /// <c>ready</c> or <c>failed</c> -- terminal, one per type -- and a pre-flight is neither. Sending it
    /// as a type event would have reported five partners as five tool-type outcomes.
    /// </remarks>
    public Task PushPreflightAsync(
        Guid jobId,
        string contentType,
        IReadOnlyList<GccGenerateService.GccPartnerToolReadiness> partners,
        CancellationToken ct = default) =>
        _hub.Clients.Group(WorkflowRealtimeHub.GccGenerateGroup(jobId))
            .SendAsync(
                "GccGeneratePreflightEvent",
                new
                {
                    jobId,
                    contentType,
                    ready = partners.Count(p => p.Ready),
                    total = partners.Count,
                    partners,
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
