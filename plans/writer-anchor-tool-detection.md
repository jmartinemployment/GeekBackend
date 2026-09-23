# GccV2CreateLibraryWriter: carry the brief to anchor-based tool detection

> **Status: not started.** The retrieval side shipped; this is the one path left
> that retrieves chunks and cannot label them. Deferred deliberately (Jeff,
> 2026-09-23) rather than bundled into the anchor-detection change, because
> threading a parameter through a draft orchestrator is a different kind of work
> from fixing a DTO that could not deserialize.

## Context

`IGeekCrawlerRagClient.QueryAsync` takes `anchorToolLookup` — host → the
operator's spelling for that partner — and `MapChunksToQuoteable` uses it to put
one `Target Entity Match: <Name>` line on a chunk whose anchors point at a
partner's own domain. Two of three callers supply it:

| Call site | Supplies a lookup? |
|---|---|
| `GccGroundingResolver.cs:165` | Yes — `GccRequiredToolMentions.AnchorLookup(create.BriefJson, project.PartnerUrls)` |
| `GccV2GeekCrawlerResearchResolver.cs:706` | Yes — `GccV2PartnerUrlResearchService.AnchorLookup(rawBriefJson)` |
| `GccV2CreateLibraryWriter.cs:622` | **No — `anchorToolLookup: null`** |

The writer passes `null` because its dependencies are `_rag`, `_providers`,
`_logger` and two flags (`:25-29`) — nothing carries a brief, a project or a
partner list into the class. That is documented at the call site so the absence
reads as a constraint rather than an oversight.

**Why it matters here specifically.** The writer already knows the partner names
and already knows they do not match what is indexed. Its own comment at `:174-176`:

> Do NOT hard-filter Qdrant by entityNames: Create puts tool labels (Melio, …) in
> TargetEntities, but indexed chunks use host/entityName payloads that rarely
> match those labels — MatchAny then returns zero pages even when the run is
> fully indexed.

Anchor detection is the missing join between those two facts: the operator's
label on one side, the chunk's outbound links on the other. Every other path got
it; this one is where the mismatch was first written down.

**Not urgent, and the plan should say so.** `DraftAsync` (`:70`) has no
production caller — the only invocations are
`GeekBackend.IntegrationTests/RagClientContractTests.cs:213,234,262`. The class
is DI-registered (`Program.cs:162`) but nothing in the app drafts through it, and
`GeekAPI/CLAUDE.md` records the surrounding v2 write path as dormant
(`ContentCreatorV2:DraftingEnabled` unset). So this closes a gap that costs no
live capability today; it should land with, or before, the first real caller.

## The finding that makes this small

**No DTO change is needed. The brief is already on the request.**

`CreateLibraryDraftRequest.CanonicalBrief` (`GccV2CreateLibraryDraftModels.cs:57`)
is a `JsonElement?`, and `GccV2GenerationBrief.ToCanonicalBrief()`
(`GccV2GenerationContracts.cs:90-128`) embeds the operator's brief verbatim as
**`rawBrief`** at `:125`. That is the same JSON
`GccV2PartnerUrlResearchService.CollectPartnerToolRows` parses, so
`AnchorLookup` works on it unchanged.

Every production request-builder already sets it: `GccV2PlanService:191,222`,
`GccV2WriteService:461,1148,1346`, `GccV2ValidateService:209`.

So the work is: read a field that is already arriving, and thread one optional
parameter down two hops. The API surface does not change, and no caller of
`DraftAsync` has to pass anything new.

**Rejected alternative: add `PartnerUrls` or a prebuilt dictionary to
`CreateLibraryDraftRequest`.** It would work, and it is worse. The canonical
brief's `operatorTools` / `targetEntities` are **names only** — no URLs — so a
caller pairing them to hosts would have to invent the pairing rule that
`AnchorLookup` already owns. The first time the two disagreed, the prompt would
name one partner two ways: `Zone & Co` in the required-mentions block and
`Zoneandco` on a retrieved chunk. The brief carries name and URL together; that
is why it is the thing to pass.

## Stages

### Stage 1 — extract the raw brief in `DraftFromCreateLibraryAsync`

In `DraftFromCreateLibraryAsync` (`:113`), alongside
`var entities = NormalizeEntities(request.TargetEntities);` (`:131`):

- Read `request.CanonicalBrief`; when present, take its `rawBrief` property.
- Serialize that element back to a JSON string and pass it to
  `GccV2PartnerUrlResearchService.AnchorLookup(...)`.
- When `CanonicalBrief` is absent, or has no `rawBrief`, or the lookup comes back
  empty, hold `null` — **not** an empty dictionary. `DetectEntityFromAnchors`
  treats both as "no label", but `null` states at the call site that no brief was
  available, which is a different fact from a brief that declares no partners.

Fail closed by omission, per `AGENTS.md`: a brief that will not parse yields no
labels, never a guessed one. No fallback that derives names from hosts here —
`AnchorLookup` already applies that fallback internally, under the precedence
rule that a brief row wins.

### Stage 2 — thread it to the query

Add `IReadOnlyDictionary<string, string>? anchorToolLookup = null` to:

1. `QueryRunsAsync` (`:551`) — two call sites, `:177` (partner) and `:183`
   (competitor). Both get the same lookup: a competitor page linking to a partner
   is still evidence about that partner.
2. `QueryRunAsync` (`:602`) — one call site, `:575`.
3. Pass it to `_rag.QueryAsync` at `:622`, replacing `anchorToolLookup: null` and
   the block comment that explains the gap.

Both methods already carry a long tail of optional parameters in the same shape,
so this follows the existing signature convention rather than introducing one.

### Stage 3 — tests

In `GeekBackend.Tests/ContentCreatorV2/`:

- A brief whose `rawBrief` carries a partner row with a URL produces a lookup,
  and a chunk anchored at that host comes back with `Target Entity Match:` and
  the **row's** spelling — the assertion that guards against a second
  name-derivation path appearing here.
- `CanonicalBrief` null, and `CanonicalBrief` present with no `rawBrief`, both
  yield unlabelled chunks and no exception.
- A brief that is valid JSON but declares no partners yields unlabelled chunks
  (distinguishes "no partners" from "no brief" only in the argument passed, not
  in the output — assert the output is the same, so neither is treated as an
  error).

The existing `RagClientContractTests` calls to `DraftAsync` (`:213,234,262`) need
no change: the parameter is optional and `CanonicalBrief` stays unset there.

### Stage 4 — verification

```bash
dotnet build GeekBackend.sln -v q --nologo          # 0 errors
dotnet test GeekBackend.Tests/GeekBackend.Tests.csproj --nologo -v q
grep -n "anchorToolLookup" GeekAPI/Services/ContentCreatorV2/Write/GccV2CreateLibraryWriter.cs
```

The last one should show the parameter on both helpers and a real argument at
`:622` — no remaining `anchorToolLookup: null` on this path.

## What this plan does not do

- **Does not touch `GccV2WriteService`.** It builds `CanonicalBrief` correctly
  already; it is dormant for other reasons (`GeekAPI/CLAUDE.md`), and waking it
  is not in scope.
- **Does not unify V1 and V2 partner services.** `GccRequiredToolMentions.AnchorLookup`
  and `GccV2PartnerUrlResearchService.AnchorLookup` are separate because the
  briefs are parsed by separate services; collapsing them is its own change.
- **Does not add a production caller for `DraftAsync`.** This plan makes the path
  correct for when one exists; it does not create one.
