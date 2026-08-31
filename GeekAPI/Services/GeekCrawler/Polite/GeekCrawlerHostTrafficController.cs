namespace GeekAPI.Services.GeekCrawler.Polite;

/// <summary>
/// Per-origin concurrency slot pool + <c>_nextAllowedTime</c> spacer.
/// </summary>
public sealed class GeekCrawlerHostTrafficController
{
    private readonly SemaphoreSlim _semaphore;
    private DateTimeOffset _nextAllowedTime = DateTimeOffset.MinValue;

    public GeekCrawlerHostTrafficController(int maxParallel)
    {
        var slots = Math.Max(1, maxParallel);
        _semaphore = new SemaphoreSlim(slots, slots);
    }

    internal DateTimeOffset NextAllowedTime => _nextAllowedTime;

    public async Task<T> ExecutePolitelyAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task EnforceCooldownAsync(TimeProvider clock, CancellationToken ct)
    {
        var wait = _nextAllowedTime - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, clock, ct).ConfigureAwait(false);
    }

    public void MarkRequestCompleted(TimeSpan chosenDelay, TimeProvider clock) =>
        _nextAllowedTime = clock.GetUtcNow() + chosenDelay;

    public void ApplyExternalCooldown(TimeSpan duration, TimeProvider clock) =>
        _nextAllowedTime = clock.GetUtcNow() + duration;
}
