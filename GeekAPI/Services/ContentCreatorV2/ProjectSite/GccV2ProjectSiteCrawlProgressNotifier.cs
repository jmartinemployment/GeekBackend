using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public sealed class GccV2ProjectSiteCrawlProgressNotifier
{
    private readonly IHubContext<GccV2RealtimeHub> _hub;

    public GccV2ProjectSiteCrawlProgressNotifier(IHubContext<GccV2RealtimeHub> hub) => _hub = hub;

    public Task PushAsync(object payload, Guid runId, string ownerUserId, CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            _hub.Clients.Group(GccV2RealtimeHub.ProjectSiteRunGroup(runId))
                .SendAsync("ProjectSiteCrawlEvent", payload, ct),
        };

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            tasks.Add(_hub.Clients.User(ownerUserId).SendAsync("ProjectSiteCrawlEvent", payload, ct));

        return Task.WhenAll(tasks);
    }
}
