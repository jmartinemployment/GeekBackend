using Microsoft.Extensions.Caching.Memory;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Remembers which user owns a crawl run, so the ingest routes stop asking GeekRepository the
/// same question on every request.
/// </summary>
/// <remarks>
/// Ingest does an ownership check first thing in every handler, and that check was a full HTTP
/// round trip to GeekRepository each time. The crawler sends a <c>pages/batch</c> and a
/// <c>links/batch</c> every couple of seconds for the length of a crawl, so a 752-page run made
/// hundreds of these lookups to re-answer a question whose answer cannot change.
///
/// <para>
/// That mattered on 2026-09-28 at 16:04:59.682. One of those lookups failed at the socket level
/// after 5.4ms — GeekRepository was healthy and served another run lookup 200/21ms in the same
/// second — and because the check runs before the handler's try/catch, the rethrow became an
/// unhandled exception and a bare 500 with no body. The crawler treats an ingest failure as fatal,
/// so 752 pages and 69,968 links were discarded over one dropped connection. Four other crawls
/// died the same way between 23 and 28 September.
/// </para>
///
/// <para>
/// §3a forbids retrying that call and says to fix the root cause instead. This is that fix: the
/// call mostly stops happening, so it mostly stops being able to fail. It is not a fallback — a
/// cache miss performs the real lookup and a real failure is still reported as a failure.
/// </para>
///
/// <para>
/// Safe to cache because <c>OwnerUserId</c> is assigned when the run is created and never
/// reassigned, so this stores an immutable fact rather than an authorization decision that could
/// later differ. Two rules keep it honest:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Only a resolved owner is cached. A run that does not exist is never remembered as absent —
/// otherwise a lookup racing the run's creation would pin "no such run" for the whole window.
/// </description></item>
/// <item><description>
/// Entries expire. A deleted run stops being vouched for, and in the interim the write it
/// authorizes fails at the repository, which is the correct outcome rather than a silent one.
/// </description></item>
/// </list>
/// </remarks>
public sealed class GeekCrawlerRunOwnerCache
{
    /// <summary>
    /// How long a resolved owner is trusted. Comfortably longer than the gap between a crawl's
    /// batches and far shorter than anything that would make a stale entry interesting.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;

    public GeekCrawlerRunOwnerCache(IMemoryCache cache) => _cache = cache;

    private static string Key(Guid runId) => $"geek-crawler:run-owner:{runId:D}";

    /// <summary>The cached owner of this run, or null when it has not been resolved.</summary>
    public string? TryGet(Guid runId) =>
        _cache.TryGetValue(Key(runId), out var value) ? value as string : null;

    /// <summary>Remember this run's owner. Blank owners are ignored rather than cached.</summary>
    public void Set(Guid runId, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return;
        _cache.Set(Key(runId), ownerUserId, Ttl);
    }

    /// <summary>
    /// Forget this run. Called when a run is deleted, so the entry does not outlive the run and
    /// vouch for something that is gone.
    /// </summary>
    public void Forget(Guid runId) => _cache.Remove(Key(runId));
}
