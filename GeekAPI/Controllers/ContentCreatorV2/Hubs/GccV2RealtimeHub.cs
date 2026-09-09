using System.Security.Claims;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.AgentTests;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Generation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ICurrentUserContext _user;
    private readonly GccV2SkillAdminPolicy _admin;
    private readonly ILogger<GccV2RealtimeHub> _logger;

    public GccV2RealtimeHub(
        HttpGccV2Repository repo,
        ICurrentUserContext user,
        GccV2SkillAdminPolicy admin,
        ILogger<GccV2RealtimeHub> logger)
    {
        _repo = repo;
        _user = user;
        _admin = admin;
        _logger = logger;
    }

    public static string JobGroup(Guid jobId) => $"job:{jobId:D}";

    public static string ProjectSiteRunGroup(Guid runId) => $"project-site:{runId:D}";
    public static string AgentTestGroup(Guid testRunId) => $"agent-test:{testRunId:D}";
    public static string ContextIngestionGroup(Guid ingestionJobId) => $"context-ingestion:{ingestionJobId:D}";
    public static string ContextIngestionOwnerGroup(string ownerUserId) =>
        $"context-ingestion-owner:{ownerUserId.ToLowerInvariant()}";

    public async Task JoinContextIngestionJob(Guid ingestionJobId)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) throw new HubException("Unauthorized");
        var job = await _repo.GetContextIngestionJobAsync(ingestionJobId, Context.ConnectionAborted);
        if (job is null || !string.Equals(job.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
            throw new HubException("Not found");
        await Groups.AddToGroupAsync(Context.ConnectionId, ContextIngestionGroup(ingestionJobId));
        await Clients.Caller.SendAsync("ContextIngestionEvent", job, Context.ConnectionAborted);
    }

    public Task LeaveContextIngestion(Guid ingestionJobId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ContextIngestionGroup(ingestionJobId));

    public async Task JoinContextIngestion(long lastSeq)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) throw new HubException("Unauthorized");
        await Groups.AddToGroupAsync(
            Context.ConnectionId, ContextIngestionOwnerGroup(userId));
        foreach (var status in new[] { "queued", "running", "ready", "failed", "cancelled" })
        {
            var jobs = await _repo.ListContextIngestionJobsAsync(
                status, limit: 200, ct: Context.ConnectionAborted);
            foreach (var job in jobs.Where(x => string.Equals(
                         x.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase)
                     && x.UpdatedAtUtc.ToUnixTimeMilliseconds() > lastSeq))
                await Clients.Caller.SendAsync(
                    "ContextIngestionEvent",
                    GccV2ContextIngestionNotifier.ToEvent(job),
                    Context.ConnectionAborted);
        }
    }

    public async Task JoinAgentTest(Guid testRunId)
    {
        if (!_user.IsAuthenticated || !_admin.IsAuthorized(_user))
            throw new HubException(_user.IsAuthenticated ? "Forbidden" : "Unauthorized");
        var run = await _repo.GetAgentTestRunAsync(testRunId, Context.ConnectionAborted);
        if (run is null) throw new HubException("Agent test run not found");
        await Groups.AddToGroupAsync(Context.ConnectionId, AgentTestGroup(testRunId));
        await Clients.Caller.SendAsync("AgentTestEvent",
            GccV2AgentTestProgressNotifier.ToEvent(run, "Snapshot"), Context.ConnectionAborted);
    }

    public Task LeaveAgentTest(Guid testRunId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AgentTestGroup(testRunId));

    public async Task JoinProjectSiteCrawl(Guid runId)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("Unauthorized");

        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, Context.ConnectionAborted);
        if (run is null)
            throw new HubException("Run not found");

        if (!string.Equals(run.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("User {UserId} denied JoinProjectSiteCrawl for {RunId}.", userId, runId);
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectSiteRunGroup(runId));
        await Clients.Caller.SendAsync(
            "ProjectSiteCrawlEvent",
            new
            {
                runId = run.Id,
                siteUrl = run.SiteUrl,
                status = run.Status,
                errorSummary = run.ErrorSummary,
            },
            Context.ConnectionAborted);
    }

    public Task LeaveProjectSiteCrawl(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectSiteRunGroup(runId));

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
}
