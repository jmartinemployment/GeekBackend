using GeekAPI.Services.GeekCrawler;
using Microsoft.Extensions.Caching.Memory;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// The ownership lookup that killed five crawls.
///
/// Every ingest handler opened with an ownership check, and that check was a full HTTP round trip
/// to GeekRepository — hundreds per crawl, all re-answering a question whose answer cannot change.
/// On 2026-09-28 at 16:04:59.682 one of them failed at the socket level after 5.4ms while
/// GeekRepository was healthy (it served another run lookup 200/21ms in the same second). Because
/// the check ran before the handler's try/catch, the rethrow was unhandled: a bare 500, empty body,
/// 22ms, and the crawler discarded 752 pages and 69,968 links. Four other crawls died the same way
/// between 23 and 28 September.
///
/// §3a forbids retrying that call and says to fix the root cause. The fix is to stop making it:
/// cache the owner, so the lookup happens once per run instead of once per batch.
/// </summary>
public sealed class GeekCrawlerRunOwnerCacheTests
{
    private static GeekCrawlerRunOwnerCache NewCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void An_unknown_run_is_not_remembered_as_anything()
    {
        var cache = NewCache();
        Assert.Null(cache.TryGet(Guid.NewGuid()));
    }

    [Fact]
    public void A_resolved_owner_is_returned_on_later_calls()
    {
        // The whole point: the second and every subsequent batch of a crawl answers from here
        // rather than crossing the network again.
        var cache = NewCache();
        var runId = Guid.NewGuid();
        var owner = Guid.NewGuid().ToString("D");

        cache.Set(runId, owner);

        Assert.Equal(owner, cache.TryGet(runId));
    }

    [Fact]
    public void A_blank_owner_is_never_cached()
    {
        // A run document with no owner is not evidence of ownership, and caching "" would make
        // every later comparison against it fail in a way that looks like a denial rather than a
        // missing field.
        var cache = NewCache();
        var runId = Guid.NewGuid();

        cache.Set(runId, "");
        Assert.Null(cache.TryGet(runId));

        cache.Set(runId, "   ");
        Assert.Null(cache.TryGet(runId));
    }

    [Fact]
    public void Forgetting_a_run_stops_it_being_vouched_for()
    {
        // Called wherever a run is deleted. Without it the entry outlives the run and keeps
        // authorizing writes against something that is gone.
        var cache = NewCache();
        var runId = Guid.NewGuid();
        cache.Set(runId, "owner-1");

        cache.Forget(runId);

        Assert.Null(cache.TryGet(runId));
    }

    [Fact]
    public void Runs_do_not_share_entries()
    {
        var cache = NewCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        cache.Set(a, "owner-a");
        cache.Set(b, "owner-b");

        Assert.Equal("owner-a", cache.TryGet(a));
        Assert.Equal("owner-b", cache.TryGet(b));

        cache.Forget(a);
        Assert.Null(cache.TryGet(a));
        Assert.Equal("owner-b", cache.TryGet(b));
    }

    [Fact]
    public void The_ttl_outlasts_the_gap_between_a_crawls_batches()
    {
        // Batches arrive seconds apart. A TTL shorter than that would leave the lookup firing on
        // most requests and the fix would not be a fix. It is also bounded, so a deleted run whose
        // Forget was missed stops being vouched for on its own.
        Assert.True(GeekCrawlerRunOwnerCache.Ttl >= TimeSpan.FromMinutes(1));
        Assert.True(GeekCrawlerRunOwnerCache.Ttl <= TimeSpan.FromHours(1));
    }
}
