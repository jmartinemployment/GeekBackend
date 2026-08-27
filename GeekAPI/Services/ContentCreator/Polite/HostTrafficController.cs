namespace GeekAPI.Services.ContentCreator.Polite;

/// <summary>
/// Per-origin serial lock + <c>_nextAllowedTime</c> spacer (not last-completed subtraction).
/// </summary>
public sealed class HostTrafficController
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private DateTimeOffset _nextAllowedTime = DateTimeOffset.MinValue;

    /// <summary>Exposed for deterministic tests (FakeTimeProvider).</summary>
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

    /// <summary>After a finished robots/page attempt: next request allowed after <paramref name="chosenDelay"/>.</summary>
    public void MarkRequestCompleted(TimeSpan chosenDelay, TimeProvider clock) =>
        _nextAllowedTime = clock.GetUtcNow() + chosenDelay;

    /// <summary>429/503: next request allowed after backoff (replaces Mark for that attempt).</summary>
    public void ApplyExternalCooldown(TimeSpan duration, TimeProvider clock) =>
        _nextAllowedTime = clock.GetUtcNow() + duration;
}
