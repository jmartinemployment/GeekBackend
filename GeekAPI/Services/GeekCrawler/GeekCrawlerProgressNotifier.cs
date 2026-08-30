using GeekAPI.Controllers.GeekCrawler.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Pushes Geek-Crawler run status to the run hub group and owning user.</summary>
public sealed class GeekCrawlerProgressNotifier
{
    private readonly IHubContext<GeekCrawlerRealtimeHub> _hub;

    public GeekCrawlerProgressNotifier(IHubContext<GeekCrawlerRealtimeHub> hub) => _hub = hub;

    public Task PushAsync(object payload, Guid runId, string ownerUserId, CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            _hub.Clients.Group(GeekCrawlerRealtimeHub.RunGroup(runId)).SendAsync("GeekCrawlerEvent", payload, ct),
        };

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            tasks.Add(_hub.Clients.User(ownerUserId).SendAsync("GeekCrawlerEvent", payload, ct));

        return Task.WhenAll(tasks);
    }
}
