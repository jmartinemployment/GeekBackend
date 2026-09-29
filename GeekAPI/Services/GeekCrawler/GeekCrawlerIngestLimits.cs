namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Ingest batch ceilings, shared by every route that enforces one.
/// </summary>
/// <remarks>
/// This class is the other side of a mirror that had nothing in it. Geek-Crawler-v2's
/// <c>src/storage/ingest-limits.ts</c> claimed to mirror a <c>GeekCrawlerIngestLimits</c> here and
/// no such type existed; the server hardcoded <c>2000</c> inline in the links route instead. Two
/// numbers that must agree, one of them a literal in a controller and the other a constant in
/// another repository, is how they drift apart without anyone noticing.
///
/// <para>
/// It matters more now than it did. While the crawler refused anything over the cap on its own
/// side, the server's copy was close to decorative — the client never sent a batch big enough to
/// test it. The cap is now high enough that the crawler will genuinely send large batches, so the
/// server number is load-bearing: if it lags, every large batch is rejected at the boundary with a
/// 400 the crawler treats as a hard failure.
/// </para>
///
/// <para>
/// What actually bounds a links batch, stated because the previous justifications were wrong in
/// both directions. It is <b>not</b> a Mongo BSON limit: links are separate documents, not one
/// document, so the 16 MiB cap never applied. It is <b>not</b> a Postgres insert ceiling: <b>crawling
/// does not use Postgres at all</b> (Jeff, 2026-09-29 — a live Postgres use in the crawl path is a
/// defect to fix on sight, not to document), and the EF layer that made this sentence necessary is
/// gone. Links go to Mongo via <c>MongoGeekCrawlerService</c>. And it is
/// <b>not</b> atomicity: <c>InsertLinksIgnoringDuplicatesAsync</c> loops <c>InsertOneAsync</c> per
/// document and swallows duplicate-key errors individually, so a batch of any size is already N
/// independent writes with no rollback.
/// </para>
///
/// <para>
/// The real bound is the HTTP request body, which the crawler caps at 28 MiB
/// (<c>MAX_BATCH_BODY_BYTES</c>). <see cref="MaxLinksPerBatch"/> is set so that a batch of ordinary
/// links sits well inside it: the netsuite.com portal page that provoked this measured roughly
/// 180 bytes per link, so 10,000 is about 1.8 MiB, and even at a pessimistic 1 KiB per link it is
/// 10 MiB. The crawler enforces the byte ceiling as well as the count, because a count cap alone
/// would just move the failure from "too many links" to an unbounded request body.
/// </para>
/// </remarks>
public static class GeekCrawlerIngestLimits
{
    /// <summary>
    /// Links accepted in one <c>links/batch</c> call. Must equal
    /// <c>MAX_LINKS_PER_BATCH</c> in Geek-Crawler-v2's <c>src/storage/ingest-limits.ts</c>.
    /// </summary>
    public const int MaxLinksPerBatch = 10_000;

    /// <summary>
    /// Pages accepted in one <c>pages/batch</c> call. Must equal
    /// <c>MAX_PAGES_PER_BATCH</c> in Geek-Crawler-v2's <c>src/storage/ingest-limits.ts</c>.
    /// </summary>
    public const int MaxPagesPerBatch = 100;
}
