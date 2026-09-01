using System.Collections.Concurrent;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Tracks in-flight crawl runs so replace-on-start can cancel active workers.</summary>
public sealed class GeekCrawlerRunCoordinator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    public CancellationToken Register(Guid runId, CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        _active.AddOrUpdate(
            runId,
            cts,
            (_, old) =>
            {
                try
                {
                    old.Cancel();
                }
                finally
                {
                    old.Dispose();
                }

                return cts;
            });

        return cts.Token;
    }

    public void Cancel(Guid runId)
    {
        if (_active.TryRemove(runId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public void Unregister(Guid runId)
    {
        if (_active.TryRemove(runId, out var cts))
            cts.Dispose();
    }
}
