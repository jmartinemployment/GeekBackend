using GeekAPI.Services.Workflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.Workflow.Hubs;

/// <summary>
/// Realtime channel for Workflow tools generation jobs at <c>/hubs/workflow-realtime</c>.
/// </summary>
[Authorize]
public sealed class WorkflowRealtimeHub : Hub
{
    private readonly ToolsGenerationJobStore _jobs;

    public WorkflowRealtimeHub(ToolsGenerationJobStore jobs) => _jobs = jobs;

    public static string ToolsJobGroup(Guid jobId) => $"tools-job:{jobId:D}";

    public async Task JoinToolsJob(Guid jobId)
    {
        var job = _jobs.Get(jobId);
        if (job is null)
            throw new HubException("Tools job not found");

        await Groups.AddToGroupAsync(Context.ConnectionId, ToolsJobGroup(jobId));
        await Clients.Caller.SendAsync("ToolsJobEvent", ToolsJobEventMapper.Map(job), Context.ConnectionAborted);
    }

    public Task LeaveToolsJob(Guid jobId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ToolsJobGroup(jobId));
}
