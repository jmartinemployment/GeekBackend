# Fix the Content Creator prompts: one static system layer, run data in the user message, failures recorded, no retries

## Context

The last Create failed and the run log did not say why. Reading the code shows two separate defects:

1. **The system message is not a system prompt.** For Pillar, Blog and Tool it carries run data: brief controls, the publisher's site block, lede continuity, evidence and retry text, revision notes, the slot list, the quotation instruction and the quotable spans, plus per-batch numbers (word share, keyword count, closing ask). No two calls share a prefix, so the rules are buried and OpenAI prefix caching cannot work. It also holds two voices ("senior consultant" and "copywriter over coffee") and the same rule stated several times (case studies, headings, tone).
2. **Failures are not recorded or are lossy** (read from `GccRunLog.cs`, `OpenAiProvider.cs`, `LlmResponseJsonParser.cs`):
   - `finish_reason` is parsed (`OpenAiCompatibleDtos.cs:59`) and never read. A truncated 16,384-token body shows up only as a parse failure.
   - A refusal arrives as `content: null`; `Clean(null)` throws `ArgumentNullException` and the `call` event records `response: null`.
   - A timeout (120 s default, `ProviderOptions.cs:60`) is an `OperationCanceledException`, which the recorder and the coordinator both skip; only the generic `failure` event notes it.
   - Only the first draft's `verdict` is recorded; the retry's verdict and which draft was kept are not (`GccGenerateService.cs:3116-3122`). The retried batch is not recorded as a `batch` event.
   - `cachedTokens` reaches `ILogger` only, not the `call` event.

The pasted assessment is used as a design tool, not literally. Its direction is right (static system layer, run data in the user message, structure instead of arithmetic). Its code is not usable as written: `framing.WhereTheyFail` does not exist (the member is `PainPoints`); choosing the quote by keyword substring would drop the "must say how the product solves the problem" rule, which nothing in code enforces; and its `GccSystemScaffolding` drops provenance, the one-quote rule and the closing rule. Its "silent crashes" explanation is only partly true; the real gaps are the five above.

Constraints that govern the fix: no retries and no fallback paths (a failed check refuses on the first attempt); the model never emits markup; no Markdown anywhere; no Postgres in crawl; no TODOs or stubs.

## Design

Three layers. Each call is `[system][user]`, and the system text is byte-identical across calls.

**1. System (one constant, all types, all calls).** Identity in one voice; the output contract; the rules that never change.
- One voice, decided by Jeff: the consultant. A senior consultant advising a prospective client, third person on Pillar and Tool, first and second person allowed on Blog (that difference is the per-type layer's, not the system's). The "copywriter explaining this to a colleague over coffee" persona is deleted, not reconciled. Its mechanics survive as constraints on how the consultant writes, not as a second identity: short sentences mixed with long, active voice, paragraphs of uneven length, no scaffolding words, no recap, one concrete detail per section, the named product rather than "leading platforms". The brief's tone-of-voice and the publisher positioning remain run data in the user message.
- Rules, each stated once, positively where possible: link text length (`GccDraftGuard.MaxLinkWords`); money in US dollars, foreign amounts omitted; content only (no `##`, no HTML, no markup characters in `text`); figures only from the supplied evidence; the banned word list; headings state this section's claim and are never bare labels or "Overview".
- Contract: run/paragraph/section JSON shapes, including `quote` with `candidate`. Provenance stays in the contract for pillar and blog (the guard needs it), so there are two byte-stable contract variants chosen by type, not by run, and a third for ledes.
- Excluded: anything depending on the project, the batch, the slot, the evidence or the revision.

**2. Per-type layer (static text per type, at the head of the user message).** What a Pillar, a Blog and a Tool page are, as obligations: the slots already in `PillarPrompts`/`BlogPrompts`/`ToolPrompts`, the page's role (the partner definition and the implementer/software distinction for Tool), and the type's structural rules (no tools section and tools-as-solution for Pillar and Blog; one quotation and its rule for Tool). Same text every call of that type.

**3. Run layer (user message).** Most stable first, so the longest prefix is shared within a run: brief controls, publisher site block, the operator's framing, evidence block, partner data, lede continuity, revision notes; then the per-call part last: assigned sections, the rest of the outline, this batch's share (stated as structure), and the closing ask only on the batch that owns the end of the page.

**The user message ends on the task, not on the evidence.** Evidence and partner data are long vendor prose and sit in the middle; the last lines of the user message are the per-call assignment and one short line pointing back at the system contract ("Return the sections array in the contract; write each heading yourself"). It is a pointer, not a second copy of the contract, so the contract is still stated once. (Raised in the design critique; the rule is cheap and keeps dense vendor text from being the last thing the model reads.)

**Word and keyword arithmetic.** Replace "count as you go" and "roughly once every 200 words" with structure the writer can follow: a paragraph count per section and a per-section floor from the slot's depth. The guard still measures words and keyword mentions after the draft; the per-batch checks in `GenerateSectionsInBatchesAsync` stay and stay refuse-on-first-attempt. `SeoKeywordMentionsFor` stays the single definition; the prompt states the number the check reads.

**Quote.** The writer keeps choosing by number from the list the guard also reads. No keyword pin. A pre-selection step is a separate decision because it changes what the page quotes.

## Few-shot examples: dropped (Jeff, 2026-10-06)

Not part of this work. The site does not have enough good examples to draw from, and the quality of the
writing was never the problem; the failures are in how the call is assembled and how its failure is
recorded. Nothing here adds an example store, a renderer for examples, or an overlap check. The existing
lede-type examples in `BuildLedeTypeGuidance` are unchanged.

## Changes

1. **System constant.** New static class beside `ContentPromptBuilder` holding the one system text and the contract variants. All long-form builders use it: `BuildPillarLedePrompt` (`:1682`), `BuildArticleSectionBatchPrompt` (`:1822`), `BuildStandaloneBlogLedePrompt` (`:2293`), `BuildStandaloneBlogBodyPrompt` (`:2366`), `BuildArticleLedePrompt` (`:1627`), `BuildToolBodyPrompt` (`:2691`), `BuildToolFaqSectionPrompt`. Line numbers as of HEAD `a28c7fd`.
2. **Move run data to the user message** in each builder. Keep every helper that produces run text (`BuildBriefBodyGuidance`, `BuildPublisherSiteBlock`, `BuildLedeContinuityBlock`, `BatchClosingInstruction`, `ClientDiagnosisInstruction`, `ToolQuotationInstruction`, `QuotableSpansBlock`, `SeoBodyInstruction`); change where it is placed, not what it computes. Note the blog puts the closing ask in user while pillar and tool put it in system; the pillar and tool move to user.
3. **Per-type text** into `PillarPrompts`/`BlogPrompts`/`ToolPrompts` as constants, not scattered across the shared builder.
4. **De-duplicate.** One case-study rule constant (`ToolCaseStudyRule` keeps its count-dependent form, in the user message); one heading rule; one voice.
5. **Finish reason.** Carry `FinishReason` on `ChatCompletionResult` as an optional trailing parameter (so `new ChatCompletionResult("x","m",null,null)` in `GccRunLogTests` still compiles); read it in `OpenAiProvider.cs` (and Anthropic's `stop_reason`); put it and `cachedTokens` in the `call` event (`GccRunLog.cs:144-157`).
6. **Fail on truncation, refusal and empty content, by name.** `finish_reason == "length"` fails the call as "truncated at {max} tokens"; `content: null` with a `refusal` field fails as "model refused: {text}" (add `refusal` to `OpenAiCompatibleMessage`); `content_filter` fails by name. Each is a first-attempt failure, no retry. This replaces the `ArgumentNullException` from `Clean(null)`.
7. **Timeouts get a record.** A `call` event for a timed-out call (prompt, model, elapsed, "timed out after {n} s") and an `outcome` event, instead of skipping `OperationCanceledException` when the caller's own token was not cancelled.
8. **Retries removed (Jeff's rule: no retries).** `GuardedDraftAsync` judges one draft and refuses or ships with its gaps reported; the per-batch shortfall retry is gone and the shortfall is reported with the draft; `RetryInstruction` is gone from every guard finding, `RetryReplaces`, `ShortfallInstruction` and the retry parameter of the three draft writers are deleted. This replaces recording the retry's verdict: there is no retry.
9. **Schema, checked before anything else is built.** Check `ContentSectionJsonSchema` emits `provenance` and `imagePrompt` as nullable unions (`["string","null"]`). Strict mode lists every key as required, so a non-null `string` forces the model to invent a value on tool, FAQ and lede calls that do not use it. I have not verified this: `Section.Provenance` is a plain `string?` property and I did not read the exporter's output. If they are not nullable, fix it in the schema, not the prompt. Do this first, because the static contract in item 1 has to match it.

## Not doing

- No `GccQuoteCoordinator` and no keyword-substring quote pin.
- No renames of `GccNicheFraming` members; the real ones are `CoreProblem`, `PainPoints`, `AutomationToPitch`.
- No retries, no fallback model, no provider switch (OpenAI stays live).
- No change to what the guards check; prompts and guards keep reading one definition.
- The V2 prompts (`GccV2ToolPagePromptBuilder`, partner and competitor extraction, `GccV2SchemaConstrainedGenerator`, carousel) are out of scope for this pass. I did not read them for this plan; they are listed so leaving them out is a decision.

## Test impact (from the read of `GeekBackend.Tests`)

Moving text between system and user breaks tests that assert on one side; they need rewriting to assert the new placement and the property they protect:
- `ContentPromptBuilderClosingCtaTests`, `ContentPromptBuilderSectionBatchTests`, `ContentPromptBuilderFillerBanTests`, `ContentPromptBuilderToolLinkTests`, `ContentPromptBuilderBriefReachTests`, `SectionHeadingCraftTests` (system versus user splits).
- `ToolPageQuotationTests` (batch 0 versus later; still true, text moves).
- `LedeLengthTests`, `OutlinePromptRulesTests`, `NoToolsSectionReachesTheOutlineTests`, `GccRetrievedEvidenceReachesThePromptTests`, `NicheFramingReachesTheWriterTests`, `GccHeadingProvenanceFormsTests`, `GccPartnerOnlyToolsTests`, `HumanRegisterTests`, `LedeEvidenceTests`.
- New: the system message is identical across two batches, two projects and a revision run, for each type (the property this change exists for). New: truncation, refusal, null content, timeout and the run log's finish reason.

## Verification

1. `dotnet build` and `dotnet test` on GeekBackend.Tests; the identical-system test fails before the change and passes after.
2. Print the assembled system and user message for Pillar, Blog and Tool for two batches and diff: system identical, user differs only in the per-call part.
3. Drive the provider with a stubbed `finish_reason: "length"`, a `refusal`, `content: null` and a timeout; each yields a named `call` and `outcome` event.
4. One Generate on a throwaway project, only once Jeff says no run is in flight (a GeekBackend push kills a running Generate). Read `GET .../generate/{jobId}/events` and confirm every call has `finishReason` and `cachedTokens`, and that a forced shortfall records both verdicts.
5. Compare cached-token counts on batch 2 and later against batch 1.

## Design critique received (an AI's input, not a decision)

It agrees with the three-layer split, the failure recording, the schema check and the overlap guard, and recommends: plain direct voice with the consultative tone from the brief; examples as files in the repo; V2 prompts out of scope. It adds the restate-the-task-last point (taken above). Its one wrong detail: it says quotes are chosen by the user; they are chosen by the writer by number and verified by the guard, which the plan keeps.

Its reason for files over a table is sound for this change: the overlap guard and the tests must read the same text as the prompt, and a file in the repo gives one diff to review. The cost is a deploy for each new example, and a GeekBackend push kills a running Generate.

## Decisions (Jeff, 2026-10-06)

- **Voice: consultant.** Not the plain direct writer the critique recommended. The system prompt names one consultant voice; the plain-English rules are constraints on it.
- **Examples: dropped** (see above). Earlier decision to keep them as repo files is withdrawn.
- **V2 prompts: out of scope** for this pass.

## Still open

- Nothing outstanding on examples.

## Open, 2026-10-07: "One failure should not kill the batch"

Jeff said this after the 2026-10-07 run, where the pillar was refused for a partner-subset finding and the
blog for two unlicensed headings, so nothing saved. It is not yet clear which failure is meant, and each
reading reverses a rule he set, so it has not been acted on:

1. **One content type refusing.** Already independent: pillar, blog and tool are caught per type
   (`GccGenerationCoordinator`), the others still save, and only a total failure fails the job. If a run
   did not behave that way, trace why.
2. **One check refusing a whole draft.** A single refusing check (partner-subset, links, numbers,
   unlicensed heading) discards the whole page. The alternative is shipping the draft with that finding
   reported as a gap, as the missing-partner and closing-link checks already do. That reverses the
   `Refuses: true` Jeff set on those checks.
3. **One section batch failing.** A provider error, truncation or unparseable batch fails the whole draft.
   The alternative is reporting that batch and keeping the rest, which ships an incomplete page and
   reverses the rule that a missing section is not a short page.

Ask Jeff which before changing anything.

Also uncommitted at this point: the valid-provenance-values list up front, the partner-naming rule in the
system message, and removal of two stale prompt lines ("a batch under its share is written again",
"Count as you go", and the closing placement line on non-closing batches).


## Test assertions, as written (2026-10-06 and 2026-10-07)

Test project `GeekBackend.Tests`. 1,886 pass after the last change. Each entry is the property the test pins,
not its implementation. The first four groups were committed (`cc4dcfa`, `303d04c`, `e55f41c`); the last two
were uncommitted when this was written.

### Schema (`Workflow/SectionSchemaNullabilityTests`) -- `cc4dcfa`

- `provenance`, `imagePrompt` and `href` are each **required** by the strict schema and each **may be null**.
  Strict mode lists every key as required, so a non-nullable field would force the model to invent a value on
  calls that do not use it. Passes against the existing schema; it pins the property, it did not drive a change.

### Provider outcomes (`ContentCreator/OpenAiProviderOutcomeTests`) -- `cc4dcfa`

- A finished answer returns its text, `FinishReason` "stop" and `CachedTokens` 8.
- An answer cut off with `finish_reason: length` **fails by name** ("output limit of 16384 tokens"), and the
  exception carries `FinishReason` "length" and the text the model had written.
- A `refusal` fails with the model's own words ("refused the request: I can't help with that.").
- `finish_reason: content_filter` fails by name.
- `content: null` with `finish_reason: stop` fails with "returned no content (finish_reason stop)", not an
  `ArgumentNullException` from a regex.
- A call the HTTP client abandons (`TaskCanceledException`) is a timeout naming the configured seconds
  ("did not answer within 90 seconds").
- The run's own cancellation stays an `OperationCanceledException`.
- Through `GccRecordingProvider`: the `call` event records `finishReason` and `cachedTokens`; a truncated call
  records its `finishReason` "length", the partial `response` text and the error.

`GccRunLogTests` changed one assertion: a failed call with nothing returned now records `response` as JSON
null instead of omitting the key.

### Single guarded pass (`ContentCreator/GccDraftIsGuardedOnceTests`) -- `303d04c`

Through the real pillar path, with a provider that counts body calls (three: six slots minus the lede, two
per call).

- A draft with an unlinked closing ships with the "scheduler link" gap in its warnings, no warning mentions a
  retry, the scheduler is not on the page, and the body was written exactly **3** times.
- A draft short of its floor and keyword ships with "word floor" and "has no heading containing" reported,
  none mentioning a retry, still **3** body calls.
- A draft with a tools-section heading is refused on that attempt: the message starts "Refused: the pillar",
  does not mention a retry, **3** body calls.
- The People Also Ask section is on the draft.
- An image-prompt failure saves the guarded draft and reports "Image prompts were not written".

Also changed in `303d04c`:

- `GccGenerateServiceProvenanceTests`: an unlicensed heading refuses on the first attempt, names "Made Up
  Subtopic", does not mention a retry, writes the body **3** times, and no request contains "HEADING
  PROVENANCE REJECTED".
- `GccDraftGuardTests`: the partner-subset finding names both sides in `Detail` (it was in the removed
  retry text); the link-text finding names the page in `Detail`. The `RetryReplaces` tests are deleted.
- `GccBatchShortfallTests`: assertions on `Instruction` and the "written again" test are deleted; `Report`
  assertions stay.
- `GccToolsSectionGuardTests`, `GccCurrencyGuardTests`: assertions on `RetryInstruction` removed or moved to
  `Detail`.

### Static system message (`Workflow/PromptBuilders/SystemPromptIsStaticTests`) -- `e55f41c`

Run for pillar body, blog body and tool body unless stated.

- The system message is **byte-identical** across batch 0, a later batch with evidence and an opening, a
  different project (other keyword, publisher, positioning) and a revision with notes.
- **None** of the run's data is in it: the keyword, publisher, positioning, titles, product name, the
  evidence text, the revision text, the slot text, "BRIEF CONTROLS", "THE OPENING THIS PAGE ALREADY HAS", a
  publisher heading, "CLOSING:", "WHAT THIS PAGE IS SCORED ON", "NO TOOLS SECTION", "TOOLS ARE THE
  SOLUTION". All of those **are** in the user message.
- It **does** hold the rules that never change: the consultant voice, "Ban filler", "delve", the US-dollar
  rule, the link-length rule, the heading rule, GROUNDING, NAMING THE PARTNER TOOLS (added 2026-10-07),
  CONTENT ONLY and the `"sections"` contract; and does not contain "over coffee" or "content marketer".
- `"provenance"` is in the contract for pillar and blog and **absent** for tool.
- The user message **ends with** "Answer in the JSON the output contract in the system message describes.
  You write each heading yourself."
- The three lede builders: pillar ledes share one system message, blog and tool ledes share another, whatever
  the page; the pillar lede contract has `"introduction"`, the blog lede contract has `"ledeType"`.
- Verified to fail on the old builder: 13 of these 14 failed against the pre-restructure
  `ContentPromptBuilder`.

Existing tests moved from the system message to the user message in `e55f41c`: brief controls and lede-type
guidance (`ContentPromptBuilderFillerBanTests`), the opening continuity block, the blog per-section budget and
the tool slot rendering (`SectionHeadingCraftTests`), and the competitor heading shown to the body writer
(`GccGenerateServiceProvenanceTests`). `HumanRegisterTests` now asserts "a senior consultant who knows this
work" and that "over coffee" is absent. `SectionHeadingCraftTests` asserts a planned heading is handed over
as `1. "Planned Two"` and is not told "you write its heading".

### Provenance values (`ContentCreator/GccHeadingProvenanceTellsTheWriterWhatResolvesTests`) -- uncommitted

- `LicensedValues` lists the valid tags by kind (`brief`, `paa`, `site`, `evidence`) and says
  "competitor: none available" for an empty kind.
- The instruction's own example is `brief:primaryIntent`; `brief:topic` is **not** in the prompt.
- `FindUnlicensedHeadings` accepts a heading tagged `brief:primaryIntent` and **rejects** `brief:topic`.
- `GccHeadingProvenanceFormsTests` was updated to the same example.

### Not tested

- No real model call. Nothing here shows how the model responds to the new layout; the first throwaway
  Generate does.
- The 2026-10-07 partner-subset refusal has no test that the prompt *prevents* it. The system message now
  states the rule; whether that changes the model's behaviour is only visible in a run.
- The valid-values list reaching the pillar and blog calls (appended where the evidence block is built in
  `GccGenerateService`) is covered only by the builder-level tests above, not by a run through the service.
