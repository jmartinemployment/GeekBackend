# GeekBackend: carry contentHtml, blocks and contentReadyAt through ingest

> **Status: implemented in `5561209`.** All four hops carry the fields, the
> external ingest route fails closed, and the internal `SameOriginBfsCrawler`
> path is unchanged. Two deviations from this plan, both found while tracing:
> `ContentReadyAt` needed the existing `PgTextDateTimeOffsetSerializer` (the
> collection came from a Postgres CSV export and stores timestamps as text), and
> the new members are `Ignore()`d in the EF model because Postgres is deprecated
> for `geek_crawler` and EF cannot map `BsonArray`.
>
> The BSON-shape unit test could not be added: the test project resolves
> MongoDB.Bson 2.28.0 via `EphemeralMongo.v2` while GeekRepository compiles
> against 2.24.0, and closing that split means bumping a driver whose
> `SharpCompress` / `Snappier` pins are deliberate security overrides. The
> runtime `Blocks.kind` query below covers it and is the stronger check.

## Context

The crawler retired Markdown and emits clean `contentHtml` plus typed `blocks`
(`Geek-Crawler-v2/plans/corpus-rebuild.md`). GeekAPI never took the rename, so
those fields are discarded on arrival and never reach Mongo.

Verified from source 2026-09-18 — `IngestPageItem`
(`GeekCrawlerIngestController.cs:860`) declares only
`Origin, Url, FinalUrl, StatusCode, RobotsAllowed, Html, FailureReason, Title,
Markdown, Excerpt`. `IngestPatchRunRequest` (`:847`) declares `MarkdownReadyAt`
and no `ContentReadyAt`. ASP.NET ignores unknown JSON properties, so every
`contentHtml`, `blocks` and `contentReadyAt` sent is dropped silently.

Measured consequence: 8 runs, 5,274 pages stored with `Html` only, `Markdown`
null, `MarkdownReadyAt` null. The RAG Library classified every page
`no_markdown` and deleted it with its Qdrant points. `chunksUpserted: 0`
everywhere. Every layer reported success.

`corpus-rebuild.md` §2e predicted ingest would fail closed until GeekAPI
matched. It failed **open**: acceptance at `:521-524` requires `Html` **or**
`Markdown`, and the crawler still sends `html`, so pages validated while their
content was thrown away. **This plan closes that boundary as well as adding the
fields** — a field arriving and being dropped must become an error, not a
success.

## Decisions (locked)

1. **`Blocks` persists as a native BSON array**, never a serialized string.
   `render_block_text` in the Python Library calls `.get("kind")` per element;
   a string blob crashes it at runtime.
2. **C# is transport, not a consumer.** Blocks pass through untyped —
   `JsonElement`/`JsonNode` at hops 1–2, `BsonArray` at hop 3. A typed C#
   mirror would be a third definition of the schema beside TypeScript and
   Python, which is the drift class being fixed here. Adding a block kind then
   requires no .NET recompile.
3. **Per-path acceptance.** The external ingest route requires extracted
   content; the internal crawler keeps Html-only acceptance.

## The two writers — and why no routing logic is needed

There are two live producers of `CreateGeekCrawlerPageItemCommand`:

| path | entry | supplies |
|---|---|---|
| **external** | `GeekCrawlerIngestController:528` ← Geek-Crawler-v2 HTTP | html, contentHtml, blocks, title, excerpt |
| **internal** | `GeekCrawlerPageBatchWriter:23` ← `SameOriginBfsCrawler` | html only — it runs no extractor |

The internal writer calls `HttpGeekCrawlerRepository.CreatePagesBatchAsync`
**directly and never passes through the ingest controller**. The paths are
already structurally separate, so strict validation placed in the ingest
controller applies to the external crawler by construction. No route branching,
no `/api/internal/...` route to add.

The internal path is live — invoked from `GccV2ProjectSiteController` and
`GeekCrawlerController` for ContentCreatorV2 project-site mapping. It must keep
working unchanged.

## The 4-hop matrix

```
Hop 1  GeekAPI ingest DTO        GeekCrawlerIngestController.cs
Hop 2  GeekAPI command DTOs      HttpClients/GeekCrawlerDtos.cs
Hop 3  GeekRepository controller Controllers/GeekCrawler/GeekCrawlerPagesController.cs
Hop 4  Mongo entity              Data/Entities/GeekCrawler/GeekCrawlerPage.cs
```

Hop 3 declares its **own duplicate** `CreateGeekCrawlerPageItem` (`:129`),
independent of hop 2's record. That duplication is exactly where a field is
lost without a compile error — both must change together.

### Hop 1 — `GeekCrawlerIngestController.cs`

- `IngestPageItem` (`:860`) — add `string? ContentHtml = null`,
  `JsonElement? Blocks = null`.
- `IngestPatchRunRequest` (`:847`) — add `DateTimeOffset? ContentReadyAt`,
  `bool ClearContentReadyAt`. Keep the Markdown pair until the Library migrates;
  map `ContentReadyAt` onto the run's readiness field.
- Both-set guard (`:244-246`) — mirror for the Content pair.
- Mapping (`:528-538`) — pass the new fields into the command.
- **Acceptance (`:516-524`) — the fail-closed fix.** Replace `Html || Markdown`
  with: a robots-allowed page carrying no `FailureReason` must have non-empty
  `ContentHtml` **and** a non-empty `Blocks` array, else the request is
  rejected `400` naming the offending URL. Log the extraction fault explicitly.

  The crawler sends **one page per request** (`persist.ts` always calls with a
  single-element array), so batch rejection is page rejection, and the crawler
  fails closed on it (`maxRequestRetries: 0`). No partial-batch semantics to
  design.

### Hop 2 — `HttpClients/GeekCrawlerDtos.cs`

- `CreateGeekCrawlerPageItemCommand` (`:54`) — add `ContentHtml`, `Blocks`.
- `GeekCrawlerPageDto` (`:33`) — the read model; add both so anything reading
  pages back sees them.
- Run patch command (`:~28`) — add the `ContentReadyAt` pair.
- `GeekCrawlerPageBatchWriter.cs:23` — **the second producer.** It supplies no
  blocks; leave its call site correct by making the new parameters optional.
  Miss this and the internal crawl fails to compile or silently passes nulls.

### Hop 3 — `GeekRepository/.../GeekCrawlerPagesController.cs`

- Duplicate `CreateGeekCrawlerPageItem` (`:129`) — add both fields.
- Mapping (`:102-117`) — set them on the entity. Convert blocks to native BSON:

  ```csharp
  var json = p.Blocks?.GetRawText() ?? "[]";
  var blocks = BsonSerializer.Deserialize<BsonArray>(json);
  ```

  No truncation helper for blocks — `TruncateTitle`/`TruncateExcerpt` exist for
  strings and must not be applied here.

### Hop 4 — entities

- `GeekCrawlerPage.cs` — add `public string? ContentHtml { get; set; }` and
  `public BsonArray? Blocks { get; set; }`.
- `GeekCrawlerRun.cs` — add `public DateTimeOffset? ContentReadyAt { get; set; }`
  beside `MarkdownReadyAt`.

## Tests to update

Three files encode the current contract and will fail loudly, which is wanted:

- `GeekBackend.IntegrationTests/GeekCrawlerE2ETests.cs`
- `GeekBackend.IntegrationTests/RagClientContractTests.cs` (asserts page
  Markdown at `:146-153`)
- `GeekBackend.IntegrationTests/E2EProtocolStubs.cs`

Add, rather than only amend:

- ingest accepts a page with `contentHtml` + `blocks` and **no** markdown
- ingest **rejects 400** a robots-allowed page with `html` but empty blocks
- the internal `GeekCrawlerPageBatchWriter` path still succeeds with html only
- a round trip proves `blocks` is a BSON **array**, not a string, and that a
  `row` block keeps its `cells`

## Verification

Per hop, not only end to end — the failure being fixed is a field vanishing
mid-chain, so each boundary gets its own assertion.

1. **Hop 1** — POST a page with `contentHtml`/`blocks`; assert 200 and that the
   command built at `:528` carries both (unit test on the mapping).
2. **Hop 3** — assert the repository controller's entity has them set.
3. **Hop 4 — the decisive check.** Against Mongo directly:
   ```
   db.crawl_pages.findOne({}, {ContentHtml:1, Blocks:1})
   db.crawl_pages.findOne({"Blocks.kind": "row"})     // matches only if native array
   db.crawl_runs.findOne({}, {ContentReadyAt:1})
   ```
   The `Blocks.kind` dotted query is the test that a string blob cannot pass.
4. **End to end** — small crawl (lightyear.cloud, ~147 pages), then
   `uv run python scripts/verify_ingest_fields.py` in Geek-Crawler-Rag, which
   already reports presence and casing and exits non-zero when a field is
   missing.

**Do not trigger indexing during verification.** The Library still requires
Markdown and `_delete_unusable` is still live, so an index run would delete the
pages being verified.

## Sequence

1. Hops 1–4 plus the acceptance fix, with tests.
2. Verify at each hop, then end to end with a small crawl.
3. Only then the Library work (`Geek-Crawler-Rag/plans/retire-markdown-from-rag.md`),
   where `block_text.py` already exists and `_delete_unusable` is retired
   **before** readiness starts waking the scheduler.

## Out of scope

- Teaching `SameOriginBfsCrawler` to extract blocks — it serves project-site
  mapping and is deliberately Html-only.
- Removing `Markdown` / `MarkdownReadyAt` / `MarkdownBackfilledAt`. They stay
  until the Library migrates; dropping them now breaks a Library that still
  reads them.
