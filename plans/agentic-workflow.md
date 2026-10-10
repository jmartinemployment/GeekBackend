# Agentic workflow: three analysts in parallel, then the writer

Written 2026-10-07 for Jeff's review, from his pasted "Parallel Agentic RAG Content Creation Workflow" prompt.
**Status: nothing is built.** Facts about the code are from the live tree at `aa7ebee`; estimates are marked as such.

## 1. What the pasted workflow asks for, and what it becomes here

The prompt runs three analysts over the retrieved context, then an editor writes from their output:

| In the prompt | What it is | What it becomes here |
|---|---|---|
| `fact_checker_agent` | exact quotes, figures, verified facts, and the gaps | **Fact sheet**: a list of facts, each with its quote and the page it came from, plus a list of gaps |
| `angle_and_hook_agent` | narrative structure, tone, two hooks | **Angle**: the opening's type (one of the twelve the lede already uses), the hook, the tone notes, inside the operator's framing |
| `guardrail_and_bias_agent` | words and traps to avoid | **Avoid list**: words, phrases and structural traps found in this evidence |
| Editor-in-Chief synthesis (first prompt) | the final content | **The writer** for that content type, handed the fact sheet, the angle and the avoid list instead of the raw evidence |
| `rag_gap_warning` | a limitation stated before the content | **A gap on the draft** and a run-log event; never text inside the page |
| Content Type Agent (second prompt) | one format-specific writer per channel, in parallel, each returning its core facts, its gaps and the asset | **The writers as they already run** (the coordinator fans out the types in parallel, tool pages per partner), with two typed fields added to what each returns: `factsUsed` and `gaps` (section 6) |
| `<content_type_rules>` | the per-format rule block | **The type's own prompt set** (`PillarPrompts`, `BlogPrompts`, `ToolPrompts` ...): already one class per type, where the rule block lives |
| the three rule blocks (thread, newsletter, long-form article) | formats | section 7: the article is the pillar and blog; the thread and the newsletter are two new short-form types |
| Editor-in-Chief (third prompt) | checks the pieces do not contradict each other, packages them by channel | **Validate and Save** (jobs 11 and 12): a code check that every piece's facts are the sheet's, and the export that already writes one file per type. No model repackages anything (section 8) |

Four things in the prompt are not carried over, each for a rule already on record:

- **XML output.** The model never emits markup. Each analyst returns JSON under a provider-enforced schema, read into a typed record (the three-stage reply fix: prevent, sanitise, refuse).
- **"Simulate three agents in one call."** They are three real calls, run in parallel (`Task.WhenAll`), each a module with one job and its own contract. One call pretending to be three is one job.
- **The editor picks "the winning strategy" from two.** Structure is owned by code (`PillarPrompts.Outline`, `BlogPrompts.OutlineFor`), and a writer is told what to do, not offered a choice. The angle analyst returns one angle; a second is kept in the run log for the operator to read, not handed to the writer.
- **The gap warning inside the output.** A page never carries a note about its own evidence. The gap is reported with the draft (the same way `partner-mentions` is today) and recorded in the run log.

## 2. How it maps onto the agreed division (plans/single-source-of-responsibility.md, section 2)

| Jeff's job | In this workflow |
|---|---|
| 1 Plan / Research | Retrieval as today (`GccGroundingResolver` and the resolvers beside it) **plus the three analysts**, run once per piece after retrieval |
| 2-10 Writers | The synthesis step, one per type, written from the fact sheet, the angle and the avoid list |
| 11 Validate / Check | The guards as today, plus two new checks (section 5) |
| 12 Save | Unchanged |

The three analysts are the inside of job 1. The writers do not retrieve, do not analyse and do not see the raw corpus.

## 3. The flow for one piece

```
retrieval (as today)  -->  evidence for this piece
                                  |
          +-----------------------+-----------------------+
          |                       |                       |
     Fact checker             Angle                  Guardrail          (three calls, in parallel)
          |                       |                       |
     fact sheet               angle                  avoid list
          |                       |                       |
     code verifies            code checks the         code strips the
     every quote              lede type is one        keyword and the
     against the passage      of the twelve           partner names
          |                       |                       |
          +-----------------------+-----------------------+
                                  |
                        the writer for this type         (lede, body batches, FAQ: as today,
                        evidence = fact sheet             with the fact sheet as its evidence)
                                  |
                        guards (as today) + fact traceability + avoid-list check
                                  |
                                save
```

**Per piece, not per run.** Pillar and blog each run their own three (same evidence, different type and voice); each tool page runs its own three over its partner's evidence. Sharing the pillar's fact sheet with the blog is an optimisation for later, after a real run shows the sheets are stable.

## 4. The three contracts

Each is a C# record, serialized with web defaults; its schema comes from `GccV2AdHocJsonSchema.For<T>` (strict-safe: `additionalProperties:false`, every property required, nullable unions for optional fields), the same machinery the body calls use. Distinct schema names: `fact-sheet`, `angle`, `avoid-list`.

**Fact sheet** (`fact-sheet`)
- `facts[]`: `claim` (one sentence, the analyst's words), `quote` (verbatim from the passage), `sourceUrl`, `sourceTitle`, `partner` (the Target Entity Match or null), `kind` (`figure` | `capability` | `limitation` | `price` | `integration` | `other`)
- `gaps[]`: `what` (what the query needed), `why` (not in the context | contradicted | outdated)
- `contradictions[]`: `a`, `b` (two quotes that disagree), `sourceUrls`

**Angle** (`angle`)
- `ledeType`: one of the twelve the lede contract already names
- `hook`: one or two sentences, the opening's first move
- `tone`: short
- `structureNotes[]`: how the fixed outline's sections should flow for this reader (never new sections)
- `alternative`: the same four fields, kept in the run log only

**Avoid list** (`avoid-list`)
- `words[]`: exact words or phrases, with `why`
- `traps[]`: structural patterns to avoid, each one sentence
- `biasNotes[]`: what in the context is marketing or circular, by source

**What each analyst is given**: the brief fields the writer gets today (keyword, audience, angle for SEO, the operator's framing), the piece's type, and the evidence block as the writer sees it now. Nothing else. The system message of each is static (prompt-cache friendly); the evidence is in the user message.

## 5. What code enforces (the analysts are models, so their output is checked like a writer's)

- **Every quote is verified.** A fact whose `quote` is not a verbatim substring of the passage at its `sourceUrl` is dropped and recorded by name in the run log (`fact-dropped`, with the quote and the URL). The remaining facts are the sheet. *Decision for Jeff (section 11, item 2): drop-and-record, or refuse the piece when any quote fails.*
- **Figures come from facts.** The existing `numbers` guard checks the draft's figures against its evidence; its evidence becomes the fact sheet's quotes, so a figure the writer invents is refused as today, and a figure the fact checker invented was already dropped above.
- **The avoid list cannot ban the page's own words.** Code removes the target keyword, every partner name and the publisher's name from `words[]` before the writer sees it; each removal is recorded.
- **The lede type is one of the twelve** (`ParseLedeTypeStrict`), or the angle is refused.
- **The avoid-list check on the draft**: a flagged word that appears in the draft is reported as a gap (`avoid-list`), not refused. *Decision, section 11 item 3.*
- **The gap report**: the fact sheet's `gaps[]` are recorded as a `gaps` event and carried on the draft as a reported gap, so the operator sees what the evidence lacked. A piece whose fact sheet is empty is refused: there is nothing to write from.
- **No retries, no fallback.** An analyst's unusable reply refuses that piece, the same way an unusable lede does today. No second call, no writing from the raw evidence instead.

## 6. The writers: what the content-type agent prompt adds

The second prompt's shape is already the shape of this codebase: one writer per type, each with its own rule block, all run in parallel by the coordinator. What it adds is **what a writer hands back beside the content**:

- `factsUsed[]`: the ids of the fact-sheet entries the piece is built on (the prompt's "3-5 core facts"). Code checks every id exists in the sheet, and that every figure in the piece appears in one of those facts' quotes. A piece that names no facts is refused: it was not written from the sheet.
- `gaps[]`: what the piece needed that the sheet did not have (the prompt's `missing_context_warnings`). Reported with the draft and recorded, like the fact checker's own gaps. Never prose inside the page.
- The asset is the typed document it is today: a `ContentDocument` for long-form (rendered by `SectionHtmlRenderer`, the one place tags are made), a typed record for short-form. Never a markdown or XML string.

The anti-hallucination block ("strict anchoring", "no inference", "the honesty gap") is the rule the fact sheet enforces structurally: the writer is given the facts and nothing else, so there is nothing to infer from. The "no inference" wording (grew, not skyrocketed) goes into every writer's system message as one shared constant.

## 7. The three format blocks

| Block | Here |
|---|---|
| **Long-form article** (intro with thesis, body sections mapping to the data points, actionable conclusion, "H2 and H3 markdown headers") | The pillar and blog as they are: the opening is the introduction, the slots are the body sections, the closing is built by code. Headings are `Section` nodes, never markdown. New: each slot's guidance names the fact-sheet kinds it should draw on (`figure`, `limitation` ...), which is "sections mapping to the data points" |
| **Thread** (4-6 connected parts; part 1 a contrarian or curiosity hook from a fact; parts 2-5 the insights; part 6 a CTA or summary; no greeting, no emojis) | A new short-form type, `thread`: `{parts:[{text, factIds[]}]}` under a schema. Code checks: 4 to 6 parts, part 1 cites a fact, the last part is the closing the code supplies for short-form (the brief's CTA, as email and social keep theirs), no emoji unless the brief allows one |
| **Newsletter** (first person, short paragraphs, dividers, a TL;DR bullet block at the top, then the breakdown) | A new short-form type, `newsletter`: a `ContentDocument` whose first section is the TL;DR as a list paragraph and whose following sections are the breakdown, so the one renderer draws it. Code checks the first paragraph is a list of 3-6 items. `ContentLengthTargets` already has `EmailNewsletter*` constants (200-400 words) that nothing uses |

Both new types are enabled only when they have their prompt set, their schema, their checks and a real run; the frontend's hand copy of the disabled list has to learn them (a separate commit in `content-creator-v2`).

## 8. The editor-in-chief is two jobs that already exist, not a model

The third prompt asks a model to receive every piece, check they agree on the core facts, and package them by channel as markdown. Here:

- **"Verify they do not contradict each other."** Every piece was written from the same verified fact sheet and names its `factsUsed`, so a contradiction can only be a piece departing from the sheet. The code check in section 6 catches that per piece; a cross-piece pass then compares the figures each piece states for the same fact id and reports any mismatch as a gap on both pieces (`fact-mismatch`). A model judging "do these read as contradictory" is the report-only Critic the agreed direction puts after the rest works; it is not in this plan.
- **"Package them into a deliverable grouped by channel."** That is the export: `GccArtifactExportService` already writes one file per type (`tools/ramp.html`, the pillar, the blog) and the frontend's Output tabs group by type. The short-form types export as their typed JSON today; a rendered file per short-form piece is an export change, not a model call. Nothing repackages prose, and nothing emits markdown.

## 9. Where it lives, and how it stays isolated

- New namespace `GeekAPI.Services.ContentCreator.Analysis`: `FactChecker`, `AngleFinder`, `Guardrailer` (one class each: prompt, schema, parse, verify), `AnalysisRunner` (runs the three in parallel, records the events, returns `PieceAnalysis`), and `FactSheetEvidence` (renders the fact sheet as the writer's evidence block, one format, one place).
- **The build enforces the boundary.** A test asserts that nothing under `Analysis` references `GccGenerateService` or `ContentPromptBuilder`, and that the only thing outside `Analysis` that references it is the coordinator's one call site.
- **Beside the old, behind a setting.** `ContentCreator:AgenticWorkflow` (appsettings, overridable by a Railway variable). Off: today's path, untouched. On: analysis runs after grounding and the writer's evidence is the fact sheet. No automatic fallback from one to the other.
- **Run log.** New event kinds, all within `varchar(32)`: `fact-sheet`, `angle`, `avoid-list`, `fact-dropped`, `gaps`. The three `call` events are recorded as every call is today. Together they are the `parallel_workflow` block the prompt wanted, in the record instead of in the output.

## 10. Order of work, each slice gated by a real run

0. **Jeff's decisions** (section 11).
1. **Contracts, schemas, parsers, verification** for the three analysts and for `factsUsed`/`gaps`, with tests and no wiring. Includes the schemas in the inventory guard test from the reply-fix plan, so the "every JSON call has a schema" rule covers them from the start.
2. **Pillar, behind the setting.** The runner wired into the pillar path only; the writer's evidence is the fact sheet and it returns `factsUsed`. Jeff runs Pillar alone with the setting on, then off, and reads the Run log: the three analysis events, the dropped quotes, the gaps, the verdict, the cost.
3. **Tool page** (per partner, inside the existing fan-out), then **Blog**; the cross-piece fact check once two types run from one sheet.
4. **Email and social as they are**: the angle and avoid list only; they carry no evidence today.
5. **Thread and newsletter**, one at a time, each with its prompt set, schema, checks, a real run, and the frontend's list.
6. **Delete the old evidence path** once every type passes a real run with the setting on; the setting goes with it.

Between slices nothing else changes. A slice can be the last; the system works at every step.

## 11. Decisions for Jeff

1. **Where it runs.** (a) In the production API behind the setting, as the agreed rule 3 says (recommended: no new infrastructure, one code path to delete later). (b) A second Railway environment of GeekAPI from a branch, which also lets a push land without restarting the API a Generate is running on; it costs a second service, a second copy of the API's variables, and the frontend needs a way to point at it. (a) and (b) are not exclusive.
2. **A quote that does not verify**: drop that fact and record it (recommended), or refuse the piece.
3. **A flagged word in the draft**: report it as a gap (recommended), or refuse.
4. **The angle's alternative**: keep it in the run log only (recommended), or show it in the Generate page's Run log view.
5. **Email and social**: angle and avoid list only (recommended), or leave them exactly as they are.
6. **Thread and newsletter**: in this plan as slice 5, or later. The UI does not add types this phase, so they would exist in the API before the page shows them.
7. **The cross-piece check**: code comparing figures by fact id (recommended), or also a report-only model critic now.

## 12. Cost and risk, stated

- **Cost (estimate, from the 2026-10-07 run log: 53.6K tokens of evidence per pillar call, 40 calls, OpenAI cache serving 54% of input).** Today a pillar's six writing calls each carry the full evidence: about 320K input tokens. With this, three analysis calls carry it (about 160K, the second and third mostly from cache) and the writing calls carry the fact sheet instead (estimate 8-12K tokens each, 50-70K in all). Net: fewer input tokens per pillar, not more, and the writer never sees the 69% of the evidence that is partner passages it will not cite. This is an estimate; the Pillar run in slice 2 is what measures it.
- **A thin fact sheet makes a thin page.** If the fact checker drops too much, the word floors are missed and the page says so in its gaps. That is the gap warning working, not a reason to fall back to the raw evidence.
- **Latency**: three calls in parallel add one call's time, not three.
- **The analysts are models.** Every one of their outputs is verified or filtered by code before a writer sees it (section 5). Nothing they say is trusted on its own.
- **Scope.** Workflow product builders are left alone; `GccV2*` dormant agent tables are not built on.

## 13. What this does not change

The closing built by code, the FAQ sources, the tool title rule, the one-page-per-type rule, the delete-first rule, the guards, the no-retry rule, the provider (OpenAI), and RAG as retrieval only. The analysts read what retrieval returned; they never retrieve.
