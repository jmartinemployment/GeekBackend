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

---

# Lockstep: retire the last of Markdown across the three services

Appended 2026-09-18. **Supersedes the §Out of scope bullet above** that keeps `Markdown`,
`MarkdownReadyAt` and `MarkdownBackfilledAt` alive "until the Library migrates; dropping them now breaks a
Library that still reads them." The Library migrated on 2026-09-18: `extract.py`, `block_text.py`,
`citation_verify.quote_in_text`, `unusable.py` (`no_content`, never `no_markdown`) and
`indexer._skip_unusable` (counts, never deletes) are all block-based, the page API returns
`PageTextResponse`, and the scheduler gates on `ContentReadyAt` via `ix_crawl_runs_content_ready`. 160
tests pass. Nothing in the Library reads a Markdown field any more, so the hold is released. Every other
line of this document stands unchanged.

## Why

Markdown is forbidden as a corpus, verification and interchange format. The crawler emits `contentHtml` +
typed `blocks`; nothing converts to Markdown at any hop. On 2026-09-18 the cost of the two halves
disagreeing was measured: every page classified `no_markdown` and 5,274 were deleted from Mongo with their
Qdrant points.

Three facts from tracing this repo drive the sequence:

1. **GeekAPI and RAG are already severed.** `HttpGeekCrawlerRagClient.GetPageMarkdownAsync:625-668`
   deserializes a `Markdown` field (`GeekCrawlerRagPageMarkdown.Markdown:898-905`) from `GET /v1/pages`.
   RAG serves `PageTextResponse` whose body field is **`text`**. So
   `GccV2PartnerExtractionVerify.cs:39-40`, `GccV2CompetitorExtractionVerify.cs:38-40` and
   `GccV2CitationEvidenceGuard.cs:44-46` all read null and **fail closed: no verified quotes, no
   citations.** Live break, fixed first.
2. **`HierarchyAssignmentMarkdown` is load-bearing, not dead.** Read at
   `ResearchBriefBuilder.cs:115,122,124-125` into the `"SITE ANALYZER ASSIGNMENT"` prompt block; also
   `ProjectSnapshotSerializer.cs:43,84,120` and `ContentGenerationOrchestrator.cs:1192`. It carries heading
   structure *and* paragraph prose. `HierarchyToolsByHeading` does not replace it. It has zero test
   coverage, as does the `PUT .../hierarchy-context` endpoint.
3. **The structured engine already exists.** `GeekAPI/Services/ContentCreatorV2/Hierarchy/` carries
   `GccV2HeadingNode.Links` as typed `GccV2HeadingLink(Name, Href)`, is wired to `GccV2Controller.cs:629,1066`,
   and `GccV2HierarchyToolMatchTests.cs` asserts `RecommendedTools` with no text parsing. Meanwhile
   `BuildHierarchyMatchesFromTrees:351-419` and `HierarchyMatchDto:339-345` have **no production caller** —
   only `PartnerToolLinkFilterTests.cs:149`. The live round trip is
   `ToolPageGenerator.ListCrawlToolsAsync:211-224` → `ExtractToolsFromTrees` → `ExtractToolsUnderMatch:503`
   → `FormatSectionAssignment` → `ExtractToolsFromAssignmentMarkdown`.

Not production, so no compatibility window: legacy fields are deleted, not deprecated, and no data
migration is written. Persistence needs none regardless — `Project` is one `jsonb` blob
(`ContentWriterV2DbContext.cs:24`), so hierarchy fields have no SQL columns. Corpus data is not deleted or
re-crawled by this work.

## Step 0 — Reconnect GeekAPI to RAG's page read

- `GeekCrawlerRagPageMarkdown` → `GeekCrawlerRagPageText`, `.Markdown` → `.Text` bound to RAG's `text`.
- `GetPageMarkdownAsync` → `GetPageTextAsync`; update the three call sites above.
- Provenance flag `MarkdownVerified` → `QuoteVerified`: `GccPartnerExtractionModels.cs:60`,
  `GccCompetitorExtractionModels.cs:83`; writers `GccV2PartnerExtractionVerify.cs:194,210`,
  `GccV2CompetitorExtractionVerify.cs:61,76`; readers `GccV2DeficitStrengthJoin.cs:105`,
  `GccV2GeekCrawlerResearchResolver.cs:481`, `GccV2ContextAdapter.cs:363`,
  `GccV2PartnerCitableBridge.cs:27,85`.
- Tests: `GccV2ExtractionQuoteVerifyTests.cs` stubs `GetPageMarkdownAsync` and asserts found/absent/offset/
  digest — the logic holds over plaintext, so this is stub + field renaming.
  `GccV2CompetitorExtractionServiceTests.cs:101` asserts the literal `"MarkdownVerified"` by reflection.

**Verify by observation, not by rename:** an extraction must return `QuoteVerified: true` against a real
indexed run. It is `false` everywhere today.

## Step 1 — Delete the V1 round trip

Delete, do not rewrite: `FormatSectionAssignment:1032-1057` (emits `#`×Level headings and
`- [Text](Href)` bullets straight from `node.Links`), `ExtractToolsFromAssignmentMarkdown:510`,
`ToolsInSlice:527`, `ParseMarkdownLinks:574`, `UniqueToolLinksLenient:588`, plus the unreachable
`BuildHierarchyMatchesFromTrees:351-419` and `HierarchyMatchDto:339-345`.

Repoint the live path at direct link reads — `UniqueToolLinks(node.Links):923-938` and
`IsLikelyPartnerToolLink:944-970` already do this with no text step; `ScoreSubtree:914-916` goes to
`CountAllToolLinks`. Keep the scoring judgement from `ParseHierarchyTools:611` ("2+ anchors accounting for
most of the node's paragraph text"), fed `node.Paragraphs` and `node.Links`.

Return real groups: `ToolsInSlice` computes `Heading` and `ExtractToolsFromAssignmentMarkdown:515-521`
throws it away, keeping only the largest group's `.Tools`. Grouping by heading already exists, unused.

`PartnerToolLinkFilterTests.cs` (~280 lines) is deleted, not renamed — every test in it verifies heuristics
that infer tools from prose. Its V2 equivalents already cover the structured path.

## Step 2 — Replace the assignment slice with structure

Do not simply delete `HierarchyAssignmentMarkdown`. Replace it with a typed
`HierarchyAssignment { Heading, Level, Paragraphs[], Children[] }` projected from `PageSectionDto`, and
render the prompt block from that. Touch points: `Project.cs:59-60`, `ProjectContracts.cs:23,56`,
`ProjectsController.cs:151-153,255`, `ProjectSnapshotSerializer.cs:43,84,120`, `GenerationRequest.cs:55`,
`ContentGenerationOrchestrator.cs:1192`, `GccV2V1ProjectBridge.cs:17`. The frontend already stopped sending
the old field.

## Step 3 — Delete `MarkdownReadyAt` (deletion, not rename)

`ContentReadyAt` already exists beside it everywhere: `MongoGeekCrawlerService.cs:147` maps the legacy
member, `:149` the new one, and `GeekCrawlerRunsController.cs:163-168` already performs the
`contentReadyAt`/`clearContentReadyAt` mutual exclusion. Renaming would collide; re-adding the check would
double it.

Remove the field, `ClearMarkdownReadyAt`, and the SignalR key together — it is actively written, cleared,
read and pushed: `GeekCrawlerRun.cs:17`, `MongoGeekCrawlerService.cs:147,837`, `GeekCrawlerDtos.cs:17,33`,
`GeekCrawlerRunsController.cs:158-189,259`, `GeekCrawlerIngestController.cs:245-254,343,887`,
`GeekCrawlerService.cs:465,566`, `GeekCrawlerEventMapper.cs:21` (`markdownReadyAt`). Leave the historical EF
migration file alone. Keep `return BadRequest("…")` — the repo forbids throwing.

Also drop write-only `GeekCrawlerPage.Markdown:18` (written every page batch, read by nothing) and
`MarkdownBackfilledAt:32` (never written). The §Tests to update section above already names
`GeekCrawlerE2ETests.cs:107,118` and `E2EProtocolStubs.cs:324,326`.

## Step 4 — The hierarchy-match endpoint

The frontend calls `/api/site-analyzer/profiles/{id}/hierarchy-match`, proxying to a GeekAPI route that no
longer exists — it died with `GccController.cs` in `582a171`. The panel already 404s and surfaces a
redeploy message, so this is visible, not silent. If restored under the restore-v1 goal, return the DTO
**with** `IReadOnlyList<ToolsByHeading> ToolsByHeading` and **without** `AssignmentMarkdown` — the shape
`normalizeHierarchyMatchFromApi` already parses. Then render the groups in `HierarchyContextPanel.tsx`,
which today only forwards them into the PUT (`:95`) and never displays them.

## Step 5 — Shared goldens: optional, unsequenced

`GccV2IntelligenceArtifactContractTests.cs` is the only consumer of the five shared fixtures and parses
them as a raw `JsonDocument` — field presence plus four literals (`artifactType`, `"coverageUnknown"`,
`"alt-co"`, one warning substring). It never deep-equals, never reads `evidenceIds` contents;
`sourceDigest` is null throughout. The Python-side regeneration changed content-derived evidence IDs, which
no C# assertion reads. Copy the fixtures across for tidiness; block nothing on it. No script or CI has ever
synced them — the last matching commits landed by hand at the same timestamp (`705a26d` / `3f210b0`).

## Not in scope

`GccV2WriteService.{ToStableMarkdown, ParseSynthesizedMarkdown, ParseMarkdownParagraphs, MarkdownToSection}`
and `LlmResponseJsonParser.{MarkdownFence, MarkdownLink}` handle **LLM output prose**, not corpus. Markdown
as a model output format is legitimate; the prohibition covers corpus, verification and interchange.

## Sequence and verification

```
Step 0 → Step 1 → Step 2 → Step 3 → Step 4 (if in scope) → Step 5 (anytime)
```

Step 0 first because nothing downstream verifies while quote verification returns nothing. Deletions follow
so a mid-sequence stop leaves a working system.

**No CI gate protects any of this.** GeekBackend's only workflow is a cron-filtered Playwright run; the
cross-repo workflow filters to `RagGenerateServiceTests|HttpGeekCrawlerRagClientTests|GccV2GeekCrawlerResearchResolverTests`
and `RagClientContractTests|GeekCrawlerE2ETests` — and it stubs the RAG response, so it asserts against a
fake that still spells `markdown` and cannot catch the Step 0 break. Presence of a contract test is not
fitness of the contract. Run all three suites locally at each step:

```
cd GeekBackend        && dotnet test GEEKBACKEND.slnx
cd Geek-Crawler-Rag   && uv run pytest -q          # 160 passed
cd content-creator-v2 && npx tsc --noEmit && npm test
```

Final sweep, expected empty apart from the *Not in scope* helpers:

```
grep -rniI markdown GeekBackend/{GeekAPI,GeekApplication,GeekRepository} \
  content-creator-v2/src Geek-Crawler-Rag/{src,scripts,tests}
```

## `ContentReadyAt` key casing — open

Read from code, **not observed on the wire**: Mongo document key is **`ContentReadyAt`**
(`MongoGeekCrawlerService.cs:149` uses `MapMember` with no `SetElementName`, so the member name is the
element name — contrast `Id` at `:143`); HTTP JSON is **`contentReadyAt`**
(`GeekCrawlerRunsController.cs:164` error text); Postgres has no column (`GeekCrawlerDbContext.cs:28`
ignores it). RAG's run filter uses Pascal `ContentReadyAt`, matching the class map. Close it by running
`Geek-Crawler-Rag/scripts/verify_ingest_fields.py` where Mongo lives — it reports presence *and* casing.
It matters because page projections hedge both casings while the run filter and index cannot.

Dropping the legacy index is safe only **after** the new RAG image is running, since Mongo errors on a hint
naming a missing index: `db.crawl_runs.dropIndex("ix_crawl_runs_markdown_ready")`.
