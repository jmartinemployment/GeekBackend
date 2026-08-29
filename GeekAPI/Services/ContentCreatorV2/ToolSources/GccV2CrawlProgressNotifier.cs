using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

/// <summary>Pushes tool-source crawl status to the run's hub group and owning user.</summary>
public sealed class GccV2CrawlProgressNotifier
{
    private readonly IHubContext<GccV2RealtimeHub> _hub;

    public GccV2CrawlProgressNotifier(IHubContext<GccV2RealtimeHub> hub) => _hub = hub;

    public Task PushAsync(object payload, Guid runId, string ownerUserId, CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            _hub.Clients.Group(GccV2RealtimeHub.CrawlGroup(runId)).SendAsync("CrawlEvent", payload, ct),
        };

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            tasks.Add(_hub.Clients.User(ownerUserId).SendAsync("CrawlEvent", payload, ct));

        return Task.WhenAll(tasks);
    }
}
