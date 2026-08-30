using System.Security.Claims;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.GeekCrawler.Hubs;

/// <summary>
/// Realtime channel for Geek-Crawler runs at <c>/hubs/geek-crawler-realtime</c>.
/// <see cref="JoinGeekCrawlerRun"/> verifies ownership and sends the latest DB snapshot on connect.
/// </summary>
[Authorize]
public sealed class GeekCrawlerRealtimeHub : Hub
{
    private readonly HttpGeekCrawlerRepository _repo;
    private readonly ILogger<GeekCrawlerRealtimeHub> _logger;

    public GeekCrawlerRealtimeHub(HttpGeekCrawlerRepository repo, ILogger<GeekCrawlerRealtimeHub> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public static string RunGroup(Guid runId) => $"geek-crawler:{runId:D}";

    public async Task JoinGeekCrawlerRun(Guid runId)
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("Unauthorized");

        var run = await _repo.GetRunAsync(runId, Context.ConnectionAborted);
        if (run is null)
            throw new HubException("Crawl run not found");

        if (!string.Equals(run.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("User {UserId} denied JoinGeekCrawlerRun for {RunId}.", userId, runId);
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RunGroup(runId));
        await Clients.Caller.SendAsync(
            "GeekCrawlerEvent",
            GeekCrawlerEventMapper.MapRun(run),
            Context.ConnectionAborted);
    }

    public Task LeaveGeekCrawlerRun(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroup(runId));
}
