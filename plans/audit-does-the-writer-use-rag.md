# Audit: does the prose writer actually use RAG?

Line-by-line trace of the live writing path, 2026-09-29. Every claim below cites
`file:line`; where I could not establish something I say so rather than infer.

**Short answer: yes, RAG reaches the prompt — and one guard makes it nearly
unusable for anything structural.** The recurring "the writer doesn't use RAG"
discovery is not wrong about the *symptom*; it has been looking at the wrong
mechanism. The pipe is connected. The licensing vocabulary at the far end does not
include what comes out of it, so a draft that builds on retrieved evidence is
thrown away.

---

## 1. The live chain, traced

```
GccController (v1 Create)
  └─ GccGroundingResolver.cs:132   anchorToolLookup = GccRequiredToolMentions.AnchorLookup(
  │                                    create.BriefJson, project.PartnerUrls)
  └─ GccGroundingResolver.cs:174   rag.QueryAsync(need, runId, crawlType, topK,
  │                                    anchorToolLookup: …)          ← REAL RAG CALL
  └─ merged into create.ResearchJson (.Quoteables)
       └─ GccGenerateService.cs:298    BuildResearchBlock(create)
       │      renders "=== QUOTEABLE RESEARCH (partner/tool evidence) ==="
       └─ GccGenerateService.cs:3145   BuildEvidenceBlock(create, competitorAnalyses)
            ├─ :2677  GeneratePillarBodyAsync  → evidenceBlock → body prompt
            └─ :2951  GenerateBlogBodyAsync    → evidenceBlock → body prompt
```

It is genuinely wired. `evidenceBlock` is not computed and dropped — at `:2705`
and `:2981` it is concatenated with the tool instruction and passed into the body
writer. The `anchorToolLookup` is built from real data and passed, so the
`Target Entity Match:` labels the prompt documents do appear.

**The docstring at `:295` records the old state and should be read as history, not
current fact:** *"Stage 2: this was the retrieved evidence GccGroundingResolver
resolves and merges into ResearchJson that pillar/blog never read back out."* That
gap was closed. Anyone re-reading that sentence today will conclude RAG is unused,
which is probably one source of the recurring rediscovery.

---

## 2. FINDING 1 — RAG evidence licenses nothing (the real defect)

`GccHeadingProvenanceGuard.FindUnlicensedHeadings` refuses any heading whose
provenance tag does not resolve against the evidence the call actually had. The
evidence is four sets, built at **`GccGenerateService.cs:3290 BuildProvenanceEvidence`**:

| set | source | line | RAG? |
|---|---|---|---|
| `populatedBriefFields` | brief **field names** (`segment`, `details`, `angle`, …) | 3306–3317 | no |
| `paaQuestions` | `brief.PaaQuestions` | 3319 | no |
| `competitorHeadings` | competitor page headings | 3322–3329 | **no — see Finding 3** |
| `siteSubtopics` | `GccMustMention.Subtopics(mustMentionBlock)` | 3331 | no |

**`create.ResearchJson.Quoteables` — the retrieved passages — is not a licensing
source.** So the model is handed RAG evidence, instructed that "every claim about a
partner or tool must trace to one of the passages below" (`:303`), and then any
heading it writes *from* that evidence is unlicensed and the draft is refused.

This is not theoretical. Observed in production **2026-09-28 13:01:39**, job
`1ebb8784` for create `e440ae46`:

```
InvalidOperationException: Blog body contains unlicensed headings:
  "Top Tools for Automated Data Entry & Processing" (h2)
  "Melio: Simplify Payments" (h3)
  "Dext: Accurate Data Capture" (h3)
  "Lightyear: Smart Invoice Processing" (h3)
  "Stampli: Enhanced Approval Workflows" (h3)
  "AvidXchange: Comprehensive AP Automation" (h3)
    provenance "site:Top 5 Automated Data Entry Processing Tools"
    does not resolve to any available source
```

Read what those headings are: **one heading per partner tool.** That is a model
doing exactly what the required-mentions block asks — naming all five partners in
structure — and being refused because the only tag available to it (`site:`) did
not resolve. There is no tag it *could* have used, because retrieved evidence has
no provenance channel.

### The asymmetry that makes this worse (Jeff, 2026-09-29)

**Only Tool *requires* a citeable blockquote. For Blog and Pillar citations are
nice-to-have, not required.** But heading provenance is enforced identically across
all three — a hard `InvalidOperationException` that discards the draft.

So on the two types where evidence is discretionary, the guard is stricter about
*structure* than the product is about *evidence*:

| | citeable quote | heading provenance |
|---|---|---|
| Tool | **required** | enforced |
| Blog | nice-to-have | **enforced — hard refusal** |
| Pillar | nice-to-have | **enforced — hard refusal** |

A Blog draft that voluntarily builds on retrieved evidence is refused. One that
ignores RAG and writes headings from brief fields passes. The incentive gradient
points away from RAG precisely where using it is optional — which is the behaviour
this audit was asked to explain.

**Consequence.** The guard does not merely fail to help RAG; it selects against it.
The safest strategy for the model is to write headings from brief fields and
competitor headings — the sources that license — and leave retrieved evidence as
decoration in body prose. Which is indistinguishable, from the outside, from "the
writer doesn't use RAG".

---

## 3. FINDING 2 — the lede and introduction are ungrounded

`GeneratePillarBodyAsync` computes `evidenceBlock` at `:2677`, then at `:2689`:

```csharp
var ledeResult = await llm.CompleteAsync(pillarType.Lede(pillarPromptCtx), ct);
```

`pillarPromptCtx` carries `context` and `metadata`. It does **not** carry
`evidenceBlock`. Same shape in `GenerateBlogBodyAsync` (`:2971`).

So the lede and introduction — the most-read paragraphs on the page, and the ones
that set every factual claim that follows — are written from brief text alone, with
the retrieved evidence sitting unused in a local variable three lines above.

---

## 4. FINDING 3 — "competitor grounding" is not RAG retrieval

`GccCompetitorAnalysisResolver.ResolveAsync` (`:51`) does two different things that
are easy to conflate:

```
:60   var indexed = await rag.HostsIndexedAsync(project.CompetitorUrls, ct);   ← RAG as a GATE
:80   var crawledPages = await pages.ListPagesBySeedsAsync(runId, urls, ct);   ← crawl store
:95   analyses.Add(new GccCompetitorPageAnalysis(page.Url, headings, declaredTypes));
```

RAG is asked only *whether* a host is indexed. The headings themselves come from the
crawl store directly — no retrieval, no ranking, no citation verification. So
`competitorHeadings`, one of the four licensing sources, bypasses the Library
entirely while appearing in code and conversation as RAG-backed grounding.

That is not necessarily wrong — reading headings out of the crawl is cheap and
deterministic — but it must not be *described* as RAG grounding, and it means three
of four licensing sources are the brief and the fourth is the raw crawl.

---

## 5. FINDING 4 — the richest RAG integration is dormant

`GccV2CreateLibraryWriter` holds the more advanced work: `QueryRunAsync:602`,
per-seed retrieval, structured evidence, `SynthesisCitations`. Per
`GeekAPI/CLAUDE.md`: *"Dormant, do not wire: `GccV2WriteService` has zero live
callers, `ContentCreatorV2:DraftingEnabled` is unset, and it has never produced a
document."*

So the second RAG query call site in the content path is in code that has never
produced output. Anyone auditing by grepping `QueryAsync` finds two call sites,
inspects the more sophisticated one, finds it unreachable, and concludes RAG is
unused. That is the second source of the recurring rediscovery.

---

## 6. What is working — do not break these

- **`anchorToolLookup` is live.** Built at `GccGroundingResolver.cs:132` from the
  brief and the project's partner URLs, passed at `:179`. So `Target Entity Match:`
  labels really do reach the prompt, and the spelling is the operator's
  (`GccRequiredToolMentions` decides precedence, not the caller).
- **The Tool path fails closed properly.** `:1704` refuses with
  `"Refused: Partner grounding required…"` when partner extraction yields too
  little, and `HasSufficientPartnerData` is the real bar. The `Refused:` prefix is
  load-bearing — `GenerateAsync`'s catch filters on it to answer 400 rather than
  503 (`:1711`).
- **Grounding refuses before generation** when the project has no partner URLs or
  none are indexed (`RequiredFor("aitool") = [Partner]`).

---

## 7. Plan to correct

Ordered by ratio of value to risk. Each is independently shippable.

### Step 1 — give retrieved evidence a provenance channel — **DONE 2026-09-29**

Shipped as one change, both halves together, 1,174 tests passing:

| file | change |
|---|---|
| `GccHeadingProvenanceGuard.cs` | 5th set `RetrievedEvidence`, `"evidence" =>` branch |
| `GccGenerateService.cs:3290` | populates it from `Quoteables` — section title, page title, host, and partner spelling via `GccRequiredToolMentions.AnchorLookup` |
| `ContentPromptBuilder.cs:289` | `evidence:<identifier>` added to the tag vocabulary the model is given |
| `ContentDocument.cs:78` | `Section.Provenance` docstring listed a stale vocabulary (no `site:`); corrected |
| `GccHeadingProvenanceGuardTests.cs` | +4 tests |

Two decisions worth keeping:

- **The partner name comes from `GccRequiredToolMentions`, not from the host.** A host
  cannot know "zoneandco" is written "Zone & Co". Using the same precedence the
  required-mentions block and the chunk labels already use means the name the prompt
  *asks* for is the name the guard *licenses* — otherwise the model would be told
  "Zone & Co" and refused for writing it.
- **It licenses, never requires.** That is the answer to why `retrieval:<url>` was
  removed on 2026-09-22 — that rule made generation *fail on missing research*. A test
  pins the difference: an empty retrieved set licenses nothing and adds no new failure.

**Also removed, because the same premise had rotted underneath them** (Jeff,
2026-09-29: the UI now offers only SERP upload; the PAA, .gov, .edu and wiki *upload
categories* are gone).

Read that precisely: the standalone **PeopleAlsoAsk upload** is gone, the
`paa:<question>` **licensing channel is not**. SERP ingest still parses
People-Also-Ask out of the saved SERP and the panel persists the operator's
selection into `brief.paaQuestions` (Jeff, 2026-09-29) -- which is why
`GccGenerateService:2764` says the questions are "already operator-curated, by
SerpIngestPanel's own selection UI, before they ever reach BriefJson". That channel
and the FAQ section gate at `:2769` both stay. I proposed deleting them as dead on
the strength of finding no *C#* producer; the producer is the frontend.

- `GccController.UploadKeywordSource` — the PAA branch and the Wiki/.edu/.gov article
  path. The category dispatch collapsed to a single SERP path with no condition.
- `GccGenerateService:358` — the origin ternary. It tested
  `RetrievalMode == "rag_chunk"` and labelled everything else `"operator-supplied"`,
  but the only producer of an operator-supplied quoteable was the upload path just
  deleted. What it actually caught was a **null** `RetrievalMode` — which is what
  `GccPartnerUrlResearchService:334` leaves on a partner page it fetched. So real
  partner evidence was announced to the model as an operator upload, which the
  surrounding instructions define as plain prose carrying none of the structure
  labels. **Evidence was being discredited by a test for a case that no longer
  existed.**

**The leftovers went too, and the reasoning that nearly kept them was wrong.** I first
argued for keeping `GccArticleHtmlExtractor.Extract` — no production caller, but four
tests used it, so deleting it "cost four rewrites for no gain". Jeff, 2026-09-29:
*"we have spent how long discussing dead code, because of it exists in code. This is
unacceptable path to lead others down."* That is the right test and it is this repo's
own: not *does this execute* but *can this be read as evidence*. An hour went into
this file's leftovers before anyone established the writer does use RAG.

So, deleted:

- `GccArticleHtmlExtractor.Extract(url, html)` — the `GccResearchCaps`-capped overload,
  with its only caller. Its four test call sites moved to `ExtractPartnerPage`; the
  uncapped assertion now compares against `GccResearchCaps.MaxParagraphsPerPage`
  directly rather than against a capped sibling that had to exist to be compared to.
- `DeleteKeywordSource`'s quoteable filter — it matched an uploaded-file URL prefix
  that nothing can produce. The SERP page and source-row removal stay; they are live.
- Every remaining `upload://` literal, fixtures and comments included. **Zero
  occurrences in C# now**, so there is nothing left to grep and misread.

### Step 1 (original plan, kept for the reasoning)

Add a fifth set to `GccHeadingProvenanceEvidence` and populate it in
`BuildProvenanceEvidence` from `create.ResearchJson.Quoteables`:

- each passage's `Section:` title
- each passage's `Target Entity Match:` name
- the page `Title` / host for passages carrying neither

Then document the tag in the prompt beside the existing ones, e.g.
`evidence:<section title>` / `evidence:<entity name>`, so the model has a tag it
can actually use for a heading built on retrieved evidence.

**Constraints.** Keep it binary and queryable — the guard's own docstring says
*"Binary, queryable… No similarity matching."* Do not soften the guard to accept
untagged headings; that would trade a false refusal for an unsourced page, which is
the failure the guard exists to prevent.

**Test first.** `GccHeadingProvenanceGuardTests` is pure-logic and already has the
shape: add a case asserting a heading tagged from a retrieved passage's section
title is licensed, and one asserting a tag naming a passage that was *not* retrieved
is still refused.

**Verification that it worked:** re-run create `e440ae46`. Those six headings
should license.

### Step 2 — ground the lede — **DONE 2026-09-29**

`BuildPillarLedePrompt` and `BuildStandaloneBlogLedePrompt` now take an
`evidenceBlock`; `PillarPrompts.Lede` and `BlogPrompts.Lede` pass `ctx.EvidenceBlock`;
`GeneratePillarBodyAsync` and `GenerateBlogBodyAsync` set it on the lede context.
`ContentTypePromptContext.EvidenceBlock` already existed — the lede context simply
never set it.

**The token budget question, answered: no slice is needed.** The block is bounded
twice before it reaches any prompt — `PromptResearchTokenCeiling` (16,000) caps what
retrieval puts into `ResearchJson` at all, and `GccResearchCaps` caps headings and
paragraphs per page at render. It is then the *same* block the body prompt already
carries, and the lede prompt's system text is shorter than the body's. If the body
fits, the lede fits.

**The research half only, and not for the reason I first gave.** I argued the
competitor block should stay out because "the lede writes no headings". Jeff,
2026-09-29: *"While you are correct normally lede paragraphs have no heading, in this
codebase they do."* Correct, and verifiable — `SectionHtmlRenderer.AppendSection`
emits the `<h2>` whenever `Section.Heading` is non-blank, the pillar lede is stored
as `pillarLede with { Tag = "h2" }`, its slot is a `SectionSlot.Cover` the writer
names, and the user block tells the model *"You write its heading."*

The real reason the competitor block stays out is narrower and holds:
`BuildCompetitorHeadingBlock` instructs the model to tag a heading
`"competitor:<exact heading text>"` and refers it to "the provenance rules". The lede
JSON contract has no provenance field and the lede prompt states no provenance rules,
so that block would cite instructions the model was never given. A test pins it.

**`LedeEvidenceInstruction` frames the block before showing it**, because the research
block is written for the body: its rule 5 says a tool with a `Target Entity Match`
line "is named in the piece and its claims are cited from those passages". True across
a whole article, wrong in three paragraphs — handed the block unframed, the opening
would answer it by listing every partner. The framing says the evidence is there to
make the opening *true*, not to get covered: no partner names unless the angle is that
one product, and no figure that no passage states.

Licenses, never requires — same property as Step 1. A create with no research renders
a byte-identical lede prompt to the one it renders today; a test asserts the equality.

| file | change |
|---|---|
| `ContentPromptBuilder.cs` | `LedeEvidenceInstruction`, + param on both lede builders |
| `PillarPrompts.cs` / `BlogPrompts.cs` | pass `ctx.EvidenceBlock` |
| `GccGenerateService.cs` | `ledeEvidence` set on both lede contexts |
| `LedeEvidenceTests.cs` | +6 tests |

### Found while doing Step 2 — three things, verified, not yet fixed

**1. The lede's heading is never provenance-checked.** `FindUnlicensedHeadings` is
called on `bodySections` only, in both `GeneratePillarBodyAsync` and
`GenerateBlogBodyAsync`; the lede is merged into the `ContentDocument` afterwards and
never passed to the guard. So the one h2 the guard does not see is the first one on the
page — while every body heading is checked with a hard refusal that discards the draft.
Whether that is a gap to close or the correct scope (the lede's slot is assigned, so
its tag would be `plan` and license trivially) is a decision, not a bug fix.

**2. The pillar lede prompt contradicts itself three ways about headings.** The system
block says *"No heading of any kind: the title is the page's only headline."*
`LedeJsonContract` gives the model nowhere to put one — `ledeType` and `paragraphs`,
annotated *"no heading: it runs directly under the page title"*. The user block says
*"Write the pillar's Lede (first H2) 1 of 6. It covers: {ledeHeading}. You write its
heading."* One prompt, three answers. This is why I got it wrong above, and it is worth
settling in the prompt rather than leaving the model to pick.

**3. The tool body never receives an evidence block at all.**
`GccGenerateService:1815` reads `await WriteToolBodyAsync(toolOutlineCtx.EvidenceBlock)`,
and `toolOutlineCtx` is constructed at `:1770` with `App`, `ToolSlug` and
`ExtractedResearchJson` — never `EvidenceBlock`, and nothing assigns it afterwards. So
`BuildToolBodyPrompt`'s `evidenceBlock` is always null on the live Create path, and the
QUOTEABLE RESEARCH block never reaches the one content type where a citeable blockquote
is *required*. Tool is not ungrounded — `ExtractedResearchJson` carries its partner
extraction — but it is grounded through a different channel than pillar and blog, and
the parameter that looks like its grounding is dead.

Related: **`ToolPrompts.Lede` has zero callers.** The tool path calls
`_prompts.BuildArticleLedePrompt(context, pillarMeta)` directly at `:1793`, bypassing
its own prompt set. That is precisely the "decision with no home" that
`IContentTypePrompts` was created to eliminate, per its own docstring.

### Step 3 — name the competitor path honestly

Either route competitor headings through RAG retrieval so they are ranked and
verifiable, **or** rename the symbols and comments so `HostsIndexedAsync` reads as
an availability gate and `ListPagesBySeedsAsync` reads as a crawl-store read.

Cheap and worth doing: the current naming is how three people concluded the writer
was RAG-grounded when one of its four evidence sources is a raw crawl read.

### Step 4 — decide the v2 writer

`GeekAPI/CLAUDE.md` says do not wire it. Then its RAG integration should not sit
there looking live. Either:

- delete `GccV2CreateLibraryWriter`'s retrieval path and record where the work went, or
- move the parts worth keeping (structured evidence assembly, `SynthesisCitations`)
  into the v1 path that actually runs.

Leaving it is the status quo that keeps producing this audit.

### Step 5 — correct the stale docstring

`GccGenerateService.cs:295` still reads *"…that pillar/blog never read back out."*
That was true and is not. Rewrite it in the past tense with the closing commit
named, per this repo's own rule that a surviving name or sentence is read as a live
claim.

---

## 8. What I did not establish

Stated plainly rather than guessed:

- ~~**Whether `ResearchJson.Quoteables` is reliably populated in practice.**~~
  **Closed 2026-09-29, by construction rather than by observation.**
  `GccGroundingResolver:218` refuses — *"Every indexed run was queried and none
  returned a citable passage for this topic"* — so `Quoteables` cannot be silently
  empty on a create that proceeded. `GccGroundingOutcome`'s own docstring names the
  conditional-drop pattern (`if (research?.Quoteables is { Count: > 0 })`) as the
  defect this replaced. Production logs could not confirm it empirically: no
  `Grounding resolved` line exists since 26 Sept because no create has run in days
  (Jeff), not because grounding is failing.
- **Whether `GEEK_RAG_CITEABLE_GENERATE_ENABLED` / `GEEK_RAG_GENERATE_ENABLED` gate
  any of this.** Both exist as Railway variables on GeekAPI production; I found no
  reader for either name in `GeekAPI/**/*.cs`. They may be dead, or read by another
  service. If they are dead, delete them — an env var that looks like a RAG kill
  switch is exactly the kind of thing that makes the next person believe RAG is off.
- **The Workflow-path orchestrator.** `CLAUDE.md` says *"Generation stays
  RAG-grounded; the grounding moves to v1's ContentGenerationOrchestrator."* I
  audited the `GccGenerateService` Create path, which is what the controller calls.
  I did not trace `ContentGenerationOrchestrator`'s own grounding, and if the
  Workflow product writes prose through it, that path needs the same audit.
