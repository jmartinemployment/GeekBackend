using System.Collections.Concurrent;
using GeekApplication.Security;

namespace GeekRepository.Auth;

/// <summary>
/// Remembers each token id until the token has expired, so a token seen once is refused the second time. Per instance: enough while
/// GeekRepository runs one instance, which is recorded in plans/credential-rotation-and-exposure-fix.md.
/// </summary>
public sealed class TokenReplayGuard
{
    private static readonly TimeSpan PurgeEvery = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly object _purgeLock = new();
    private DateTimeOffset _nextPurge;

    public TokenReplayGuard(TimeProvider time)
    {
        _time = time;
        _nextPurge = time.GetUtcNow() + PurgeEvery;
    }

    /// <summary>True the first time <paramref name="tokenId"/> is seen; false for every later presentation before it expires.</summary>
    public bool TryRegister(string tokenId, DateTimeOffset expires)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        var now = _time.GetUtcNow();
        Purge(now);
        // Kept past the instant the token stops validating (exp plus one skew) by a further skew, so the purge cannot forget an id
        // while a clock reading a moment earlier than ours would still accept the token.
        return _seen.TryAdd(tokenId, expires + GeekRepositoryToken.ClockSkew + GeekRepositoryToken.ClockSkew);
    }

    private void Purge(DateTimeOffset now)
    {
        if (now < _nextPurge)
            return;
        lock (_purgeLock)
        {
            if (now < _nextPurge)
                return;
            foreach (var entry in _seen)
            {
                if (entry.Value <= now)
                    _seen.TryRemove(entry);
            }
            _nextPurge = now + PurgeEvery;
        }
    }
}
