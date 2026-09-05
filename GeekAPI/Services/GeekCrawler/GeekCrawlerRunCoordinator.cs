using System.Collections.Concurrent;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Cancels in-flight <see cref="GeekCrawlerService.ExecuteRunAsync"/> work.</summary>
public sealed class GeekCrawlerRunCoordinator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    /// <summary>
    /// Registers a run for execution. Returns <c>false</c> if the run is already in flight
    /// (duplicate Wake) so the caller skips a second Execute that would Unregister and
    /// dispose the shared token mid-fetch.
    /// </summary>
    public bool TryRegister(Guid runId, out CancellationToken token)
    {
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(runId, cts))
        {
            cts.Dispose();
            token = _active.TryGetValue(runId, out var existing)
                ? existing.Token
                : CancellationToken.None;
            return false;
        }

        token = cts.Token;
        return true;
    }

    /// <summary>Legacy helper for tests and callers that always start work.</summary>
    public CancellationToken Register(Guid runId)
    {
        TryRegister(runId, out var token);
        return token;
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
