using GeekAPI.Controllers.Workflow.Hubs;
using GeekAPI.Services.Workflow.Services;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>Pushes Workflow tools job status to the job hub group.</summary>
public sealed class ToolsJobProgressNotifier
{
    private readonly IHubContext<WorkflowRealtimeHub> _hub;

    public ToolsJobProgressNotifier(IHubContext<WorkflowRealtimeHub> hub) => _hub = hub;

    public Task PushAsync(ToolsGenerationJob job, CancellationToken ct = default) =>
        _hub.Clients.Group(WorkflowRealtimeHub.ToolsJobGroup(job.Id))
            .SendAsync("ToolsJobEvent", ToolsJobEventMapper.Map(job), ct);
}

internal static class ToolsJobEventMapper
{
    public static object Map(ToolsGenerationJob job) =>
        new
        {
            jobId = job.Id,
            projectId = job.ProjectId,
            kind = job.Kind,
            status = job.Status,
            completed = job.Completed,
            total = job.Total,
            error = job.Error,
            contentSet = job.Result,
        };
}
