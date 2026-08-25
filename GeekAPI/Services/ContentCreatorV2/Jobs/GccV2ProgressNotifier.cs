using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>Pushes a job event to the job's group and (if known) directly to the owning user.</summary>
public sealed class GccV2ProgressNotifier
{
    private readonly IHubContext<GccV2RealtimeHub> _hub;

    public GccV2ProgressNotifier(IHubContext<GccV2RealtimeHub> hub) => _hub = hub;

    public Task PushAsync(Guid jobId, Guid ownerUserId, GccV2JobEventDto evt, CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            _hub.Clients.Group(GccV2RealtimeHub.JobGroup(jobId)).SendAsync("JobEvent", evt, ct),
        };

        if (ownerUserId != Guid.Empty)
            tasks.Add(_hub.Clients.User(ownerUserId.ToString()).SendAsync("JobEvent", evt, ct));

        return Task.WhenAll(tasks);
    }
}
