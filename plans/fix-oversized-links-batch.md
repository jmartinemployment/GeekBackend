# Fix: oversized links batch fails a crawl outright

A crawl of https://www.netsuite.com/portal/home.shtml failed and its data was
purged. The recorded error, verbatim:

```
links/batch size 5779 exceeds atomic max 2000
```

Nothing reached the server. The crawler's own client refuses the batch before
sending:

| location | what it does |
|---|---|
| `src/storage/geek-api-client.ts:294` | `async createLinksBatch(runId, links)` |
| `src/storage/geek-api-client.ts:304-307` | throws `PersistenceError` when `links.length > MAX_LINKS_PER_BATCH` |
| `src/storage/ingest-limits.ts:10` | `MAX_LINKS_PER_BATCH = 2_000` |
| `src/storage/persist.ts:589` | the caller: `saveLinks(pageId, links)` |

So one page carried 5,779 links (a portal/sitemap-style page) and no layer splits
them. Pages have the same guard shape at `geek-api-client.ts:236` with
`MAX_PAGES_PER_BATCH = 100`, so the asymmetry is that something evidently keeps
page batches under their cap while links are handed over whole.

Server side, for context:

```
GeekBackend/GeekAPI/Controllers/GeekCrawler/GeekCrawlerIngestController.cs:664
    if (request.Links.Count > 2000) return BadRequest("at most 2000 links per batch");
```

It then forwards to `_repo.CreateLinksBatchAsync` → GeekRepository (Postgres).

## Two facts that bear on the design choice

1. `ingest-limits.ts`'s header says the limits exist to *"Stay 2 MiB under
   Mongo's 16 MiB BSON cap."* **That reason does not apply to links**: links go to
   Postgres via GeekRepository, not Mongo. 5,779 links is roughly 1 MB of JSON
   against a `MAX_BATCH_BODY_BYTES` of 28 MiB. So 2,000 may be stricter than
   links actually need.

2. The same header claims the file is a *"mirror of GeekAPI
   GeekCrawlerIngestLimits"*. **That class does not exist** in GeekBackend — the
   server hardcodes `2000` inline at the line above. The mirror is a claim with
   nothing on the other side.

## Decide and implement ONE of

### (a) Chunk in the crawler

Split links into `<= MAX_LINKS_PER_BATCH` slices at or above `persist.ts:589`
and submit sequentially. Removes the failure class for any link count.

Cost: the "atomic" batch becomes several transactions, so state whether partial
links for one page are acceptable — the count check at
`geek-api-client.ts:318` currently asserts `count === submitted` per call.

### (b) Raise the cap

Requires changing **both** `ingest-limits.ts:10` **and** the hardcoded `2000` in
`GeekCrawlerIngestController.cs:664`, and justifying the new number against
`MAX_BATCH_BODY_BYTES` and Postgres insert size. Simpler and keeps atomicity, but
it is a treadmill: the next site has 50,000 links.

## Either way

Fix the drift: introduce the `GeekCrawlerIngestLimits` the crawler already claims
to mirror, or correct that comment. A server magic number that the client
believes it mirrors is how these two drift apart silently.

## Constraints

From `GeekBackend/AGENTS.md` and `Geek-Crawler-Rag/plans/rules.md` §3a:

- No retries, no fallbacks, no silent degradation. Fail closed on a real error.
- **Truncation is prohibited** (`ingest-limits.ts` header). Never drop links to fit.
- Do not report success for a batch that did not fully land.

## Tests

`src/storage/geek-api-client.test.ts` already covers links/batch size and the
count assertion (lines ~43, ~92, ~288, ~304). Extend those rather than adding a
parallel suite. Add a case for the 5,779-link shape that failed.

## Sequencing

Only the "raise the cap" option touches `GeekCrawlerIngestController.cs` (near
line 664); `plans/fix-superseded-purge-halt.md` in this same directory touches that
file near 146–372. If you chunk in the crawler instead, there is no overlap at all.
