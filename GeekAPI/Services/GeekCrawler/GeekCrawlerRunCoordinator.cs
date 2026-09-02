using System.Collections.Concurrent;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Cancels in-flight <see cref="GeekCrawlerService.ExecuteRunAsync"/> work.</summary>
public sealed class GeekCrawlerRunCoordinator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    public CancellationToken Register(Guid runId)
    {
        var cts = new CancellationTokenSource();
        _active.AddOrUpdate(
            runId,
            cts,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return cts;
            });
        return cts.Token;
    }

    public void Cancel(Guid runId)
    {
        if (_active.TryGetValue(runId, out var cts))
            cts.Cancel();
    }

    public void Unregister(Guid runId)
    {
        if (_active.TryRemove(runId, out var cts))
            cts.Dispose();
    }
}
