# Single Source of Responsibility: one agent, one job

Written 2026-10-07 for Jeff's review. **Status: nothing is built.** Code facts below come from a full read of
`GccGenerateService.cs` and a coupling sweep of the live Content Creator path at HEAD `f4d881b`, by two read-only
explorers; I spot-checked the caller and dead-code claims, and the rest are the explorers' counts, marked as such. Where
something was not determined it says so.

## 1. Context and goal

Weeks of Content Creator failures share one cause: one concept lives in many copies, so a change to one breaks the
others. Jeff's primary goal is **Single Source of Responsibility**: every unit of the application has one job and one
reason to change. The 2026-10-07 run's four refusals each traced to our own rule or prompt, not to the model (see
`fix-the-2026-10-07-generate-run.md`). A rewrite alone did not fix it earlier; the fix has to be enforced by structure.

## 2. The division (Jeff's list, agreed)

| # | Agent | One job |
|---|---|---|
| 1 | **Plan / Research** | Read the brief and the three sources (Project site, Partners, Competitors); give each writer its slots and the evidence it needs |
| 2 | **Pillar Write** | Write the pillar page |
| 3 | **Blog Write** | Write the blog post |
| 4 | **Tool Write** | Write one tool page per partner |
| 5, 6, 7 ... | **Other long-form writers** | Comparison, Alternatives, Case Study, How-to Guide, Service Page, Whitepaper, Landing Page, **LinkedIn document (PDF carousel)** [added 2026-10-09 — now has an approved plan, `plans/linkedin-document.md`, written 2026-10-08]. Each stays disabled until it has an approved plan (existing rule). **The PDF is not a repurposed type** — it is written fresh from the brief and the evidence, like Blog, never from an existing Pillar/Blog/Tool page (Jeff: nothing produced yet is good enough to repurpose). Tools, Pillar and Blog are the types the disabled Repurpose path (section 6, step 0) applies to; the PDF writer must not be built on that path |
| 8, 9, 10 | **Short-form writers** | Email (cold outreach), Social (and ads types), Image prompt |
| 11 | **Validate / Check** | Run the guards on a finished page and return findings. Never repairs |
| 12 | **Save** | Persist the pieces |

A writer owns everything about its own type: prompts, slots, closing and FAQ, metadata and image prompts. It does not
retrieve, validate, save, or know any other writer. The coordinator that runs the agents for a request (delete first, keep
what wrote, settle) is not in the list and stays plain code. **Jeff to confirm.** What each source is for:
`docs/content-creator-sources.md`. "Agent" means a unit with one job and a typed contract; only the writers (and later a
report-only critic) call a model.

**Responsibility at three levels:** the agent; the call (only its own slots); the evidence (decided by Plan/Research and
never by the writer). Evidence has one source, retrieved once per Generate and held in the running job's memory; the
checks read all of it; each call gets a view, `evidence.For(slot)`: the whole set first, selection later.

## 3. Rules (Jeff agreed, 2026-10-07)

1. **Isolation enforced by the build.** A writer cannot reach into another writer's code, and no writer reaches past the
   shared kernel and the Plan/Research, Validate and Save agents. A test fails if one does.
2. **A real run gates each piece.** Jeff runs it and reads the Run log; nothing counts as fixed before that.
3. **Built beside the old, no automatic fallback.** A setting Jeff controls picks which runs; the old code is deleted once
   the new one passes a real run.

Standing rules for every agent: no blind retries; fail closed (a refused piece costs one page, never the run); the model
returns content, never markup; RAG is retrieval and verification only; OpenAI is the provider; guards read the whole
evidence set; every call is recorded in the run log; the closing is built by code (`GccClosing`).
**Reasoned repair** (one bounded, logged attempt by the owning writer after Validate's finding) is Jeff's proposal, not
confirmed. Proposed limits: one attempt per page, only the failing section, a cost cap, every attempt logged, the same
checks on the result, refused if the repair fails, never repaired by code editing text. **Never for length:** a
word-floor shortfall is not repaired, because it was tried: the 2026-10-06 12:52 run re-asked per batch and per guard,
was refused anyway, and cost a second call for nothing (Jeff); and the 2026-10-07 log shows the writer stops on its own at
about 640 words per two-section call, so asking again fails for the same reason. Length is fixed by sizing the outline in
the Plan agent. `CLAUDE.md` section 2 forbids repair until Jeff changes that section.

## 4. What the code looks like today (the evidence)

- **`GccGenerateService.cs`, 3,798 lines, one class, 14 constructor arguments.** Pillar (2492-2668), blog (2715-2848),
  tool (748-878, 880-1019, 1335-1753), email, social, image prompts, Revise, evidence assembly, guard inputs, batching.
  `GenerateToolPageAsync` does about seven jobs in 419 lines. Pillar and blog are near-clones (about 15 lines of setup
  copied verbatim). Nothing in the file persists anything; save and export are fragments inside each writer.
- **A content type is consulted at 47 sites in 15 files** (explorer count): the coordinator's dispatch and validation,
  the disabled list (13 keys), the grounding tables, the prompt registry, the export's three switches, controllers,
  Revise, SEO and length tables, and more. Tool dispatch happens three times (`GccGenerationCoordinator` 595, then
  `GccGenerateService` 839, then 930). `IsToolType` matches `"tool"` only and misses `"aitool"`. Blog's minimum length is
  2,000 words in one table and 1,800 in another. The frontend keeps its own hand copy of the disabled list.
- **`ContentPromptBuilder.cs`, 3,444 lines, shared with the Workflow product.** The system message and its helpers are
  shared by pillar, blog and tool; the lede constants are shared; tool-only and pillar/blog-only constants exist. Nine or
  more builders are Workflow-only. The pillar FAQ prompt carries neither the currency nor the link-text rule, though the
  guard checks the FAQ for both. Type names are hard-coded inside the builder (lines 1937, 2433, 2839).
- **Guards:** `GccDraftGuard` has `Pillar`, `Blog` and `Tool` entry points; `Pillar` and `Blog` share one body.
  `GuardInputsFor` and `GuardedDraftAsync` are one instance method each, shared by all three types.
- **Tests:** 14 test files construct `GccGenerateService` (8 directly, 6 through `GccToolPageFanOutFixture`); 34 reference
  `ContentPromptBuilder`; 16 reference the guards. No test instantiates the coordinator or calls `RunGenerateAsync`.
  `GenerateBlogBodyAsync`, `GenerateEmailAsync` and `GenerateSocialPostAsync` have no direct test calls.

## 5. Where today's code goes

- **Plan / Research:** `BuildBriefFieldsBlock`, `BuildBriefOnlyBlock`, `BuildResearchBlock` (321-445, three renderers in
  one), `BuildPublisherPositionsBlock`, `BuildOwnSiteCoverageBlock`, `BuildCompetitorResearchBlock`,
  `BuildCompetitorHeadingBlock`, `BuildEvidenceBlock`, `WithPublisherPositions`, `WithForeignAmountsNamed`,
  `PartnerUrlsForAsync`, `PartnerToolsAsync`, `ResolveCompetitorAnalysesAsync`, `BuildProvenanceEvidence`;
  `GccGroundingResolver`, `GccCompetitorAnalysisResolver`, `GccPublisherProfileResolver`.
- **Pillar, Blog, Tool, short-form writers:** the methods named in section 4, each with its own prompts moved out of
  `ContentPromptBuilder` into the writer, its lede/body/FAQ builders, its closing and its metadata.
- **Validate:** `GuardedDraftAsync`, `GuardInputsFor`, `RequireCompleteMetadata`, `BatchShortfalls`, the sufficiency gate
  in `AssessPartnerToolReadinessAsync`, `ValidateBriefRequired`, the disabled-types check; `GccDraftGuard` and its siblings.
- **Save:** the envelope, JSON-LD and URL fragments now embedded in each writer; persistence in the coordinator;
  `GccArtifactExportService`.
- **Shared kernel (stays):** `GetLlm`, `GccRunLog`, `ExtractBriefFields`, `GccClosing`, `GccDraft`, rule constants such
  as `MaxLinkWords`.
- **The content-type definition** replaces the 47 consultations: key, label, enabled and why not, slots, which sources it
  retrieves and whether it must cite them, which checks apply, export rules, length targets.
- **Left alone:** the Workflow product and its builders.

## 6. Order of work

0. **Delete what is provably dead** (no behaviour change; build and tests prove it): `SiteAnalysisDto`,
   `ValidateImagePromptRequiresLongForm` and `ToDisplayType`, `ValidAudienceDetails`, `ExtractBriefAudienceAngle`,
   `SerializeAnalysisPayload`, `ParseAnalysisPayload`, `DeserializeGaps`, `DeserializeSitePages`,
   `TryBuildSectionContext`, `BuildEvidence`, `HierarchyMatchDto`, `ToolsByHeading`, `ToolExtractDiag`,
   `ParsePackVariants`, `PackVariant`, `BuildToolResearchExtractionPrompt`, the disabled Repurpose path
   (`RepurposeInternal`, `GenerateRepurposePackAsync`, `GenerateToolAsync`, `SerializeDocument`'s caller) and the generic-blog
   tail of `GenerateStartingContentAsync`. Jeff's yes first.
1. **Gate, now: Jeff runs Pillar alone on the current code** and sends the Run log events:
   `SELECT at, kind, piece, payload_json FROM content_creator.gcc_generate_job_events WHERE at >= '<run start>' AND kind IN ('verdict','outcome','settled','fault','failure','warning','completed') ORDER BY job_id, seq;`
   Nothing below starts before this.
2. **Pillar Write, new, beside the old**, with Plan/Research, Validate and Save extracted as the pillar needs them. Passes
   on a real run: a clean verdict, the page ends on its closing, no refusal, cost near $0.40. Then the old pillar is deleted.

   **Progress, 2026-10-09 (a pure, behaviour-preserving extraction only -- not the agent itself).** Before this, line
   counts on this section's two source files had already moved 11 commits past the 2026-10-07 evidence above:
   `GccGenerateService.cs` was 3,987 lines and `ContentPromptBuilder.cs` 3,617; no `PillarWrite`/`PillarWriter` type
   existed anywhere. What moved tonight, by caller-grep against the current code rather than against this file's
   stale evidence:
   - New `GeekAPI/Services/ContentCreator/Writers/GccGenerateService.Pillar.cs` -- `GeneratePillarBodyAsync`, as a
     `partial class GccGenerateService` split (same compiled type, same constructor, same every caller; a partial
     split changes only which file the source text sits in).
   - New `GeekAPI/Services/ContentCreator/Writers/ContentPromptBuilder.Pillar.cs` -- `BuildArticleMetadataPrompt` +
     `ArticleMetadataJsonContract`, `BuildPillarLedePrompt`, `BuildArticleSectionBatchPrompt`,
     `BuildArticleFaqSectionPrompt`, `BuildIntroductionSectionGuidance`, `BuildBenefitsSectionGuidance`,
     `BuildBestPracticesSectionGuidance`, `BuildFutureTrendsSectionGuidance`, `BuildConcretenessRevisionAmplifier`,
     `NotesAskForConcreteness`, `IntroductionJsonContract`, `LedeAndIntroductionJsonContract` -- as a
     `partial class ContentPromptBuilder` split, same reasoning.
   - Confirmed build/test state: `dotnet build GeekAPI/GeekAPI.csproj` clean (20 pre-existing nullable warnings,
     0 new, 0 errors); `dotnet test GeekBackend.Tests` **2124/2124**, the exact baseline immediately before this
     change, run twice for certainty. No test moved, none newly passed or failed.

   **A real defect this caught, worth keeping as a method note for Tool Write and Blog Write (step 3).**
   `IntroductionJsonContract`/`LedeAndIntroductionJsonContract` were `private static readonly string` *fields*
   referencing `LedeJsonContract`/`ParagraphJsonShape`/`SectionJsonContract`, which stayed behind in
   `ContentPromptBuilder.cs`. Static field initializer order **across** a partial class's files is left unspecified
   by C# (it is strict top-to-bottom only *within* one file); here the new file's fields ran first in practice, read
   the base file's fields as null, and silently dropped the entire lede JSON contract from every pillar lede prompt --
   caught only because `dotnet test` was held to the exact baseline count, by
   `LedeLengthTests.EveryLedePromptAsksForAHeading("pillar")` failing with "the lede contract is missing from the
   prompt entirely". Fixed by changing both to expression-bodied `static string X => ...` properties, which have no
   initialization order to get wrong. **Any future slice that splits a class with a `static readonly`/`const` field
   whose initializer references another field left in a different file needs this same conversion** -- a method
   moves safely with no analogous risk (it only reads fields at call time, long after all of a type's static fields,
   across every one of its partial files, have finished initializing); only a *field whose initializer references
   another field in a different file* is at risk, and it is risk whether or not the first test run happens to catch
   it.

   **Cross-pipeline finding, not in this plan's section 4/5 evidence (which predates it by those same 11 commits).**
   Caller-grepping each candidate method before moving it (rather than trusting the method's name) turned up two
   more live callers of several of these "Pillar-only" methods than this file accounts for:
   - `BuildPillarLedePrompt`, `BuildArticleSectionBatchPrompt` and `BuildArticleFaqSectionPrompt` are each also
     called from `ContentGenerationOrchestrator.cs` (`GeekAPI/Services/Workflow/Services/`) -- the Workflow
     product's own, separate pillar pipeline, which this plan's section 5 says to leave alone but does not say
     depends on these.
   - `BuildArticleMetadataPrompt` is reached by a **third** live pillar-generation pipeline this plan never
     mentions at all: `GccV2WriteService.GeneratePillarMetadataAsync` in `GeekAPI/Services/ContentCreatorV2/Write/`.
   - `BuildIntroductionSectionGuidance`, `BuildBenefitsSectionGuidance`, `BuildBestPracticesSectionGuidance`,
     `BuildFutureTrendsSectionGuidance`, `BuildConcretenessRevisionAmplifier` and `NotesAskForConcreteness` are each
     also called from `BuildArticleSectionPrompt` (singular, `ContentPromptBuilder.cs`), which in turn is called
     only by `ContentGenerationOrchestrator.cs` -- shared between Pillar's real path and a method that is
     otherwise Workflow-only.

   None of this made tonight's move unsafe (a partial-class split changes where source text lives, never which type
   it compiles into, so both other pipelines keep resolving these calls exactly as before). It does mean: **this
   file physically holding "Pillar Write" cannot yet become a standalone class with its own identity, and the old
   `GccGenerateService` pillar path cannot be deleted (per step 2's own "then the old pillar is deleted"), without
   first checking whether `ContentGenerationOrchestrator` and `GccV2WriteService` still need what they're currently
   calling.** That check is not done. Before step 2 moves from "extracted" to "real Pillar Write agent," re-run this
   same caller-grep against whatever HEAD is current then -- this file goes stale on exactly this evidence every
   time it is not re-verified, which is the lesson section 4 itself already states about an earlier version of this
   same evidence.

   **Explicitly named but left alone, confirmed shared (not ambiguous, just not Pillar-only):**
   `BuildPillarContext` (despite its name -- `GenerateBlogBodyAsync` calls it too, `GccGenerateService.cs` ~2808);
   `BuildBriefBodyGuidance` (reached by Pillar, Blog, Tool, Social and Cold Outreach bodies alike); `LedeJsonContract`,
   `LedeAskInstruction`, `LedeNoLinksReminder`, `LedeEvidenceInstruction`, `LedeLengthInstruction`,
   `LedeHeadingInstruction`, `BuildLedeContinuityBlock` (all shared across the Pillar/Blog/Tool lede and body
   builders). **Confirmed Workflow-only, zero caller on Pillar's real path, correctly left out:**
   `BuildArticleLedePrompt` (Tool's lede builder, not Pillar's), `BuildArticleMetaRevisionPrompt`,
   `BuildArticleSectionPrompt` (singular), `BuildImplementationSectionGuidance`.

   **Still not done, per the coordinator's explicit scope for tonight:** the Plan/Research, Validate and Save
   agents (section 2 items 1, 11, 12) do not exist; `GeneratePillarBodyAsync` still calls the same shared
   `GccGenerateService`/`ContentPromptBuilder` helpers it always did, now just reached across two files of one
   type rather than one -- it is not yet isolated from them, so rule 1 (section 3, build-enforced isolation) does
   not hold yet and was not attempted. No old/new switch (section 6 step 2's "a setting Jeff controls picks which
   runs," section 3 rule 3) exists. No real Generate run has happened against tonight's change -- per the hard
   constraint this was done under, that gate needs Jeff, from his own side, against production or a real
   environment; this session had no production access and did not attempt one. Step 3 (Tool Write, Blog Write, the
   short-form writers) has not started. Step 4 (the one content-type definition) has not started.
3. **Tool Write** (per-partner fan-out, extraction, the quote guard), then **Blog Write**, then the **short-form writers**.
4. **One content-type definition** replaces the 47 consultations; the frontend reads it instead of its hand copy
   (a separate commit in `content-creator-v2`).

Between slices nothing else changes. A slice can be the last; the system works at every step.

## 7. Verification

- Per slice: a pinning test through the real coordinator with a scripted model that passes on the old and the new code
  (the coordinator has none today); an isolation test (namespace references); `dotnet test GeekBackend.Tests` green
  (baseline 1,968); the 14 files that construct `GccGenerateService` keep passing or move with their fixture.
- Per slice, a real Generate by Jeff. Evidence: the `verdict`, `outcome`, `settled` and `fault` events. Assistants do not
  query the database.
- Deletion criterion: a grep for the old method names returns nothing.
- Scripted tests prove wiring and the checks. They prove nothing about model behaviour.

## 8. Risks

- Moving a live path without a model-side safety net. The pinning test and the real run per slice are the answer.
- Prompt wording moves from the shared builder into each writer, so wording will drift between writers. That is chosen over
  coupling; rule constants stay single (`MaxLinkWords` is already read by the builder and the guard).
- Pillar and blog are near-clones; extracting them may expose differences nobody intended. Each is pinned first.
- The dormant `content_creator_v2` agent tables (about thirty, no live caller, never ran end to end) are not built on.

## 9. To settle with Jeff

1. The coordinator stays plain code and outside the agent list?
2. The switch setting in `appsettings.json` or a Railway variable?
3. Step 0 (dead-code deletion) now, or after the Pillar run?
4. Reasoned repair: confirm the limits, and change `CLAUDE.md` section 2 if yes?
5. The Project-site definition (the "already published" block conflicts with rewrites; decision pending) and the Competitor
   definition, in Jeff's words.
6. The frontend's disabled list: moves to the single definition in the same round, or after?
