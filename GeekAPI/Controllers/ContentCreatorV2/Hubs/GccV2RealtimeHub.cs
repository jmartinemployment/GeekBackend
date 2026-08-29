using System.Security.Claims;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ToolSources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.ContentCreatorV2.Hubs;

/// <summary>
/// Realtime channel for Content Creator v2 jobs, mapped at <c>/hubs/gcc-v2-realtime</c>.
/// <see cref="JoinJob"/> verifies ownership then replays events after <c>lastSeq</c> before the
/// caller starts receiving live pushes — no polling required to catch up after a reconnect.
/// </summary>
[Authorize]
public sealed class GccV2RealtimeHub : Hub
{
    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2RealtimeHub> _logger;

    public GccV2RealtimeHub(HttpGccV2Repository repo, ILogger<GccV2RealtimeHub> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public static string JobGroup(Guid jobId) => $"job:{jobId:D}";

    public static string CrawlGroup(Guid runId) => $"crawl:{runId:D}";

    public async Task JoinJob(Guid jobId, int lastSeq)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("Unauthorized");

        var job = await _repo.GetJobAsync(jobId, Context.ConnectionAborted);
        if (job is null)
            throw new HubException("Job not found");

        if (!string.Equals(job.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("User {UserId} denied JoinJob for {JobId} (not owner).", userId, jobId);
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, JobGroup(jobId));

        var events = await _repo.GetJobEventsAsync(jobId, lastSeq, Context.ConnectionAborted);
        foreach (var evt in events)
        {
            await Clients.Caller.SendAsync("JobEvent", evt, Context.ConnectionAborted);
        }
    }

    public Task LeaveJob(Guid jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, JobGroup(jobId));

    /// <summary>
    /// Joins a tool-source crawl run group and sends the latest DB snapshot — no polling required.
    /// </summary>
    public async Task JoinCrawlRun(Guid runId)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("Unauthorized");

        var run = await _repo.GetToolSourceCrawlRunAsync(runId, Context.ConnectionAborted);
        if (run is null)
            throw new HubException("Crawl run not found");

        if (!string.Equals(run.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("User {UserId} denied JoinCrawlRun for {RunId}.", userId, runId);
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CrawlGroup(runId));
        await Clients.Caller.SendAsync(
            "CrawlEvent",
            GccV2ToolSourceCrawlEventMapper.MapRun(run),
            Context.ConnectionAborted);
    }

    public Task LeaveCrawlRun(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CrawlGroup(runId));
}
