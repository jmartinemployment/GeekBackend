using System.Security.Claims;
using GeekAPI.Services.ContentCreator;
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
    private readonly GccJobStore _gccJobs;

    public WorkflowRealtimeHub(ToolsGenerationJobStore jobs, GccJobStore gccJobs)
    {
        _jobs = jobs;
        _gccJobs = gccJobs;
    }

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

    public static string GccGenerateGroup(Guid jobId) => $"gcc-generate:{jobId:D}";

    /// <summary>
    /// Join a Content Creator generate job and immediately receive its current state, so a
    /// reconnect catches up without polling.
    /// </summary>
    /// <remarks>
    /// Authorises against the job's owner. JoinToolsJob above checks only that the job exists,
    /// which lets any authenticated caller attach to another user's job; generate carries create
    /// content, so it checks the subject. An unknown id is "not found", never a silent join that
    /// leaves the caller waiting on events that will never arrive -- the job store is in-memory, so
    /// a redeploy genuinely loses jobs and the client has to be told that rather than spin.
    /// </remarks>
    public async Task JoinGccGenerate(Guid jobId)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) throw new HubException("Unauthorized");

        var job = _gccJobs.Get(jobId);
        if (job is null || !SameUser(job.OwnerUserId, userId))
            throw new HubException("Generate job not found");

        await Groups.AddToGroupAsync(Context.ConnectionId, GccGenerateGroup(jobId));
        await Clients.Caller.SendAsync(
            "GccGenerateEvent", GccGenerateEventMapper.Map(job), Context.ConnectionAborted);
    }

    public Task LeaveGccGenerate(Guid jobId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GccGenerateGroup(jobId));

    /// <summary>
    /// The job records ICurrentUserContext.UserId (a Guid) while the token carries `sub` as a
    /// string, and the two can be formatted differently. Compare as Guids when both parse, so an
    /// owner is never refused their own job over brace or casing differences, and fall back to an
    /// ordinal comparison otherwise rather than letting a mismatch pass.
    /// </summary>
    private static bool SameUser(string jobOwner, string caller) =>
        Guid.TryParse(jobOwner, out var a) && Guid.TryParse(caller, out var b)
            ? a == b
            : string.Equals(jobOwner, caller, StringComparison.OrdinalIgnoreCase);
}
