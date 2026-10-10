# Fix for the 2026-10-07 Generate run

Written 2026-10-07, revised three times the same day: after a design review, after Jeff removed the partner rule,
and after Jeff's answers and the fixed closing wording (section 5). Evidence: the run's own record,
`plans/10-07-26-Logging` (76 events from `content_creator.gcc_generate_job_events`, saved by Jeff), read against
the code at `e55f41c` plus the uncommitted working tree.

**Jeff has confirmed that `plans/10-07-26-Logging` (23.8 MB, every prompt and response of the run) and the other
untracked files in `plans/` stay out of commits.** Add files by name; never `git add -A`.

## 1. What happened

Run `08:16:20` UTC, project 9156ed4d, topic "Accounts Payable: Automated Approval Workflows", five types
requested, 260 seconds, 40 model calls, 76 events, `recordingFailures: 0`.

| Piece | Result | Why |
|---|---|---|
| social | written | no guard runs on social or email |
| email-cold-outreach | written | same |
| tool: Ramp | written, clean, 2,337 words | |
| tool: Approvalmax | written, clean, 2,388 words | |
| tool: Stampli | written, clean, 2,120 words | |
| pillar | refused, `partner-subset` | four sentences named some of the five partners and not the rest: **a rule Jeff has now ordered removed** (3.2) |
| blog | refused, `heading-provenance` | two h2 tagged `brief:topic`, which the guard never licensed |
| tool: Bill | refused, `questions-quiz` | the closing section wrote "Ask yourself questions such as: ..." |
| tool: Melio | refused, `links` | the page's **opening** ended "Ready to make a change? Book now", linked to https://melio.com |

The job then **failed as a whole and saved nothing**: all five good pieces were discarded, and the delete-first
step had already removed every requested type's old pages. At list prices that is about $2.50 of model calls
(my estimate from the logged tokens: 1.22 M prompt tokens of which 0.66 M cached, 31 K completion) for no page.

What the log rules out: nothing was truncated or refused by the model (all 40 calls ended `stop`), the static
system message does its job for caching (48,000 to 51,000 prompt tokens cached on every later call of a page),
and the failure was recorded in full (`verdict`, `outcome` and `failure` events carry the drafts and the reasons).

## 2. Why the whole run failed: the code contradicts the recorded decision

`GccGenerationCoordinator.RunGenerateAsync`, multi-type branch, throws as soon as any type failed
(`GccGenerationCoordinator.cs:316-319`), **before** `PersistAllAsync` (`:328`). Its comment (`:263-266`) still
carries Jeff's 2026-09-23 rule, "One failure fails all, for now".

Jeff's later decision, 2026-10-06, is in `AGENTS.md` (gitignored): *"When some types write, those save and the
refused ones stay empty ... A type that failed is named in `refusals` and stays deleted from the delete-first
step; the others still ship. Only when every requested type fails does the job fail with nothing saved."*
Only the tool-partner fan-out implements it (`TypeOutcome.SoftFailures`). Whole-type failures do not.

Jeff said on 2026-10-07: **"One failure should not kill the batch."** So the work is to make the code do what
was already decided.

## 3. The fix

### 3.1 A refused type no longer discards the types that wrote

**The decision, extracted so it can be tested.** New `GeekAPI/Services/ContentCreator/GccRunSettlement.cs`
(internal, pure). `TypeOutcome`, now a private record in the coordinator, becomes internal like `GeneratedPiece`.

- `TypeAttempt(Type, Outcome?, Error?)`, `TypeRefusal(Type, Text)` with one `Line` = `"{Type}: {Text}"`, and
  `Settle(attempts)`.
- `Settle` fails closed on impossible states: no attempts; an attempt that both wrote and failed, or neither;
  a type that "wrote" zero pieces. Each throws, naming the type.
- When **no type wrote**: throw the **same** joined message as today, `"pillar: ... | blog: ..."`, in request
  order, so the job still fails and says why.
- Otherwise it returns the pieces of the types that wrote (request order) and the refusals (request order): a
  failed type is one entry; a written tool type contributes each partner refusal.

**The caller** (`GccGenerationCoordinator.cs`, multi-type branch, moved into an `AttemptAsync` and a
`RunTypesAsync`, so the wiring can be tested too):

1. `AttemptAsync` holds the existing try/catch verbatim: records the `outcome` event, logs, rethrows
   cancellation, turns anything else into that type's refusal.
2. Fan-out, then `Settle`, **before** any repository call.
3. `WithMissingToolPagesNamedAsync` on the pieces that wrote. It was built for exactly this case (a pillar that
   links a tool page the run did not write) and now sees only written pieces.
4. `PersistAllAsync`: one save of the pieces that wrote, as now.
5. Warnings per piece. The old loop matched `p.ContentType == attempt.Type`, which silently drops warnings when
   the request spells a type differently from its lower-cased piece; pairing by piece removes that.
6. Refusals: add each to `refusals` and push it with `onTypeOutcome(type, null, error)` after the save (the
   established "refused" push; tool partner refusals already use it).
7. One new run-log event, `settled` (what was saved, what was refused), recorded **after** the save so it never
   claims a piece that was not kept. `Kind` is a free `varchar(32)`, so no migration.
8. The result keeps its shape, `{ created, refusals, preflight, warnings }`. No third job status: the hub, the
   column and the frontend do not need one.

**Refusal text.** A failed type is `"{type}: {error}"`. Tool partner refusals gain a `tool: ` prefix
(`"tool: Bill: Refused: ..."`). Read from the frontend handlers, not observed in a run: the live hub note is
`` `${contentType}: ${error}` `` and the recorded one is de-duplicated with `includes`, so without the prefix
each partner refusal prints twice on a page that watched the run. No backend test pins the old form. This also
makes the frontend comment ("already typed on the backend") true.

**Unchanged on purpose:** delete-first stays first (`:226`; Jeff, 2026-10-06: a refused run must not leave
the previous run's page to be read as this run's); the all-or-nothing save of the pieces that wrote; the
single-type path (which only gets the same `RefusalLine`); cancellation propagating; the failure when nothing
wrote.

**Tool pre-flight.** `GccGenerateService.cs` ~744-748 runs `AssessPartnerToolReadinessAsync` per partner with
no catch, and that call deserialises a banked extraction and makes an LLM call, so one partner's failure fails
every tool page. Catch per partner (except cancellation) and record a not-ready verdict, `Coverage: "could not
be assessed: {message}"`, `PagesAttempted: 0` (unknown). The existing second phase then refuses that partner by
name with its existing wording, so no second refusal path exists. Fix the remark on `GccPartnerToolReadiness`
("A verdict, never a throw"), which is false for bank faults.

**Stale text to rewrite** (a superseded comment is read as a live claim): the "one failure fails all" comments
in the coordinator (~263-270, ~326-327, ~456-460, ~579-583, ~764-767), `GccGenerateService.cs` ~684-688,
`GccGroundingResolver.cs` ~245-247, test comments at `GccToolPagesPerPartnerTests.cs` lines 15 and 73 and
`GccGroundingRetrievalTests.cs` ~393, and the `Kind` list in `ProjectEntities.cs:328` (it omits `started` and
`batch` already; add `settled`).

**Nothing else needs to change** (checked): `GccGenerateJobRunner` never inspects the result; the job store and
`CompleteAsync` just store it; `LatestGenerate` passes it through; `PushTypeAsync` already maps
`(type, null, error)` to `failed`; the frontend's `savedByRun`/`lastRunLines` count `created`, and `showRecorded`
already shows `refusals` as "Not written (N)".

**Outcome for the 2026-10-07 run under this change:** job `ready` after the same 260 s; email, social and the
Ramp, Approvalmax and Stampli pages saved; "Not written" lists the blog, Bill and Melio (the pillar would have
passed once the partner rule is gone); those pages stay empty (delete-first); the refused drafts are in the run
log's `verdict` events; the log shows `outcome` x5, `settled`, `completed`.

**Tests** (new `GccPartialRunTests`, no existing test calls `RunGenerateAsync`):

- `Settle`: a failing type does not discard the types that wrote, using the 2026-10-07 shape exactly (pillar and
  blog refused; tool wrote three and refused two; email and social wrote) with the expected pieces and the four
  refusal lines; every type failing throws the joined message in request order; one type writing is enough; a
  type's own refusals stay together in request order; nothing attempted is not a success; a write with no piece
  and an attempt that both wrote and refused each throw.
- `AttemptAsync`: a refused type is that type's outcome, not the run's; any other fault (`HttpRequestException`)
  too; cancellation propagates, including `TaskCanceledException`; the two `outcome` events are recorded.
- `RunTypesAsync` against a fake repository (lift the one in `GccGenerateSaveTests` rather than copy it again;
  `GccToolPageFanOutFixture` supplies the service): one type failing saves the rest in **one** call and the run
  completes, with `created` and `refusals` as expected; every type failing throws and the repository is never
  asked for anything (this pins that `Settle` runs before the repository reads); refusals are pushed after the
  save in request order; a pillar saved beside a failed tool type carries the "this run did not write it"
  warning; cancellation fails the run and saves nothing; a save the repository refuses still fails the run.
- Pre-flight (`GccToolPagePreflightTests`, with a `FailOn` option on `FakeExtractionBank`): one partner's
  assessment throws, that partner is refused by name ("could not be assessed", the scripted message) and the
  others are drafted; the readiness report carries a not-ready row for it; cancellation still propagates; every
  partner failing refuses each by name.

### 3.2 Remove the causes of the refusals

No guard other than the one Jeff ordered removed is changed: the rest still refuse, there are no retries, and
stripping a bad link from a draft would be auto-repair. What changes is that the prompts stop contradicting them.

**Tools are written in the context of the keyword (Jeff, 2026-10-07).** A tool is not generic: each is written
within the context of the keyword and the problem it solves, for example "Accounts Payable: Automated Approval
Workflows", and the tool page's title is "Tool name: Automated Approval Workflows". The prompts already ask for
that ("work through the solutions the tool provides ... only what it does about this problem"). One gap to
decide (section 5): today the stored title is only the product name, `title = tool.Name`
(`GccGenerateService.cs:875`), and `Revise` reads that same field back as the product name (~1018-1029), so
changing the title means adding a separate product-name field to the page, not only editing one line.

**Blog, `brief:topic`.** Already fixed in the working tree: the provenance rule's own example was `brief:topic`,
which was never licensable, and the first-attempt prompt now lists the values that resolve
(`GccHeadingProvenanceGuard.LicensedValues`). Committed in unit 1.

**Pillar, `partner-subset`: the rule is removed (Jeff, 2026-10-07).** I read "Which implies the other two do
not?" (2026-10-06) as a rule about sentence shape and wrote a guard that refuses any sentence naming some of the
partners and not all. Jeff says that took the remark out of its context and made a rule from it. It goes, whole,
and nothing replaces it. Removed:

- the `partner-subset` finding in `GccDraftGuard` (`:195-216`) and `GccRequiredToolMentions.PartialLists`, its
  record and the sentence-splitting helper if nothing else uses it (`NamesAsWord` stays: `Named`, for tools the
  project does not list, uses it);
- the "Name them all together, or one at a time -- never some of them ..." paragraph in
  `GccRequiredToolMentions.Instruction` (`:402-406`);
- the system-message rule `PartnerNamingInstruction` (uncommitted, never released) and its test assertion
  (`SystemPromptIsStaticTests.cs:149`);
- the tests that pin the rule (`GccDraftGuardTests.cs` ~205-261), and the bullet that documents it in
  `AGENTS.md` ("Partners are named all together or one at a time, never some (`partner-subset`, 2026-10-06)"; the
  file is gitignored, so this does not appear in a diff).

What stays: `partner-mentions` (every partner named somewhere on the page, a reported gap, not a refusal),
`unlisted-tools` (a tool the project does not list is never named) and the "ALL n MUST BE NAMED" requirement
that Jeff set on 2026-09-23.

**Contradictions that remain, fixed:**

| Where | What it says | Change |
|---|---|---|
| `ImplementerPositioning`, `appsettings.json:21` and the default in `ContentGenerationOrchestrator.cs` ~1860 | "In every pillar Tools section, for each major platform covered, explain ..." plus "(e.g. Apex, LWC)" "(e.g. Agentforce)"; injected into the pillar, tool and lede prompts as "Publisher positioning" and, in the pillar lede, under a "Tone:" label | replace with Jeff's sentence (section 5); fix the default's doc comment ("in pillar Tools sections"); replace the pillar lede's "Tone: {positioning}" line with a real tone line |
| the home page's tool rosters, and the must-cover block built from them | "Top 5 Automated Approval Workflow Tools: Tipalti, ApprovalMax, Ramp, Bill, Stampli", and a must-cover subtopic "Top 5 Automated Approval Workflow Tools:" | **Jeff, 2026-10-07: remove them; the tools are now entered in the brief form.** Drop any subtopic that enumerates tools from `GccMustMention.Format` (one definition in `GccToolsSectionGuard`, `EnumeratesTools`, extracted from `IsListing`), and drop the "Top N ... Tools:" line and the tool-name line under it from the home-page text the writer reads (`GccPublisherProfileResolver`) |
| the pillar lede's research brief | `AppendKnownToolsBrief` ("name these tools wherever each is relevant") reaches the pillar lede, whose own rule says the opening names no partner or tool | a new `ResearchBriefPhase.Opening` without the known-tools block, used by the pillar lede only |
| the consultant appendix, `GccGenerateService.BuildConsultantAppendix` (~438-474), sent to every pillar and blog body call | a second voice ("Voice: objective, authoritative, technical, analytical (newspaper-style). Use first-person plural or objective third-person advisor.") and "Close with an FAQ drawn from the People Also Ask ... Keep temperature low." | **Jeff, 2026-10-07: remove the Voice line.** Remove it, "Close with an FAQ ..." (the FAQ is appended by code) and "Keep temperature low."; keep the first sentence, "Assume peer-level technical knowledge; high scannability", the four phases, "ban AI filler / clichés" and "Emit no markup of any kind" |

With the roster gone the unlisted tool Tipalti is no longer printed beside the rule not to name it, so no
separate redaction of the profile text is planned.

**The opening carries no booking ask and no link to a vendor (the Melio refusal).** The pillar, blog and tool
ledes have no rule about a call to action, and the tool lede prompt contained the raw setting `book_now` three
times ("CTA: book_now" in the lede-type guidance and in the brief block, "Call to action: book_now"); the writer
read it as a button label and linked the vendor's homepage. Remove the `CTA:` line from `BuildLedeTypeGuidance`,
and add one `LedeAskInstruction` to all four lede builders (`BuildArticleLedePrompt`, `BuildPillarLedePrompt`,
`BuildBlogLedePrompt`, `BuildStandaloneBlogLedePrompt`) next to the length and heading rules: the opening ends on
its own material and asks nothing of the reader, because the page's one ask is at its end; a link in the opening
goes only to a page whose address is printed in the evidence.

### 3.3 The closing is the same on every page, and the page writes it (the Bill refusal)

**What it is (Jeff, 2026-10-07).** For each content type, the page ends with "Answer these questions when
booking your free consultation", followed by the questions Jeff entered under "One question per line" in the
brief form, and the link to `#consultationAppointment2xl`.

**What went wrong, in plain words.** On the Bill page the writer was told to end by "asking for book_now": the
prompt printed the setting's internal code (the brief form's "Book Now" option is stored as `book_now`) instead
of a description. It then wrote the questions three times: first as a self-quiz ("Ask yourself questions such
as ..."), which the quiz guard refuses, then paraphrased, then correctly in the booking line. And the visible
words of the link were the anchor itself, `#consultationAppointment2xl`, instead of words. The writer controls the
wording, the position, the link text and the questions, and one of five pages got it wrong in a way that
discards the page.

**Proposed: the page builds the closing; the writer does not.** The wording is fixed, so there is nothing for the
writer to decide. After the last body section, before the FAQ, code appends two paragraphs built from
`ContentDocument` nodes (so `SectionHtmlRenderer` stays the only thing that makes tags): a text paragraph of three
runs, "Answer these questions when ", the link run "booking your free consultation" with href
`#consultationAppointment2xl`, and "."; then a list paragraph of Jeff's questions, one per item, in the order he
entered them. With no questions the line is "Book your free consultation." linked the same way. The wording
lives in config beside `ConsultationAnchorHref` and `ConsultationCtaLabel`, so Jeff can change it without a
deploy of code.

- **The writer is told its last section ends on its own material and that the page adds the booking line.** The
  long closing block (`BatchClosingInstruction`, `ClosingCallToActionInstruction`, `ClientDiagnosisInstruction`,
  about three thousand characters of negatives) is replaced by that one line, and **the questions no longer
  reach the model at all**, so they cannot be turned into a quiz.
  - **Built (`5cb59b1`), with one leftover (2026-10-08):** the one line is sent only when
    `GenerationRequest.PageBuildsClosing` is true, which `GccGenerateService` alone sets (`:1969`); the
    flag defaults to false (`GenerationRequest.cs:84`), so `ClosingCallToActionInstruction` — with the raw
    CtaType, "asking for book_now" — survives for any other `ContentPromptBuilder` caller. Delete the
    flag and the old block (`Geek-Crawler-Rag/plans/fix-from-the-audit.md` X10b).
- **Guards:** `closing-link` holds by construction; `questions-quiz` stays as it is, as a check on the body text
  (the page's own closing contains none of its phrases). Image prompts, the length and keyword checks, the
  currency and link checks all see the closing paragraphs like any other.
- **`ctaType` and the CTA label from the brief no longer drive the long-form closing.** Drop the `CTA:` line from
  the long-form brief controls (it says "weave naturally into closing"). The short-form email and social prompts
  keep theirs and still print the raw token (`BuildAudience`); that is not part of this run's failures.
- **To check when building it:** `GccClosingCtaGuard` finds the closing section; the `Revise` path
  (`GccGenerateService` ~1000-1060) rebuilds a body and must add the closing the same way, once; the pillar's
  People Also Ask section and the tool page's FAQ stay after it.
- **If Jeff would rather the writer write it** (prompt only): state the fixed sentence in the closing block, put
  the questions once in the last paragraph, add a static system-message line naming the six quiz phrases, render
  the CTA type in words. Smaller, but it keeps all three failure modes available to the writer.

**Tests:** a closing test (new `GccClosingLineTests`): with questions the paragraphs are exactly the fixed lead,
the linked words with href `#consultationAppointment2xl`, and a list of the questions in order; with none, the
plain line; appended to the end of the last body section and not after the FAQ, for pillar, blog and tool; the
closing prompt no longer contains the questions or `book_now`; a draft whose writer wrote no ask passes
`closing-link`; the guard's six quiz phrases are absent from the built closing; and the existing closing-prompt
tests that pin the old block are rewritten to pin the property (the writer is told the page adds the booking line).

### 3.4 The order of work, one reviewable commit each

Each is committed only after Jeff says so, and nothing is pushed while a Generate is running (a push to main
restarts the API and kills it).

1. **Commit what is already done**, with the system-message partner rule deleted from the tree first: the
   first-attempt valid-values list, the two stale prompt lines, and their tests (`GccGenerateService.cs`,
   `GccHeadingProvenanceGuard.cs`, `ContentPromptBuilder.cs`, `GccHeadingProvenanceFormsTests.cs`,
   `SystemPromptIsStaticTests.cs`, `GccHeadingProvenanceTellsTheWriterWhatResolvesTests.cs`).
2. **Remove the partner rule** (3.2): guard finding, helper, instruction paragraph, tests, the `AGENTS.md` bullet.
3. **Keep what wrote, and the pre-flight catch** (3.1).
4. **Remove the contradictions** (3.2 table, positioning, rosters, lede, appendix, the opening's no-ask rule).
5. **The closing** (3.3).
6. **The tool page title** "{Tool name}: {keyword}". The keyword comes from `GccTopic.KeywordOf(create.Topic)`
   (the topic without its department). The page's envelope gets a separate product-name field beside `title`
   (today `title = tool.Name`, `GccGenerateService.cs:875`); `Revise` (~1018-1029) and the slug read the product
   name from that field instead of from `title`, with a read of older pages that have only `title`; the
   exported page title follows `title`; the URL stays `/tools/.../ramp`. Tests: the envelope's title and
   product name for a tool page; `Revise` still finds the product; the slug is unchanged. Existing tool pages
   keep the old title until they are generated again (a re-run replaces the page).

7. **Each call carries only the passages for its own sections** (Jeff, 2026-10-07: "map passages to specific
   section slots programmatically before firing the API request, in this pass"). After units 3 to 5, in two
   commits, designed 2026-10-07 from the run log and the code (section 3.5).

### Notes from an outside review of this plan (2026-10-07), checked against the code

- **"Hardcoded tool lists in `GccToolsSectionGuard` and `GccPublisherProfileResolver`"**: there are none. Both
  files mention tool names only in comments. The roster is in the crawled home-page text and the site-structure
  subtopics, so the purge is done where the plan puts it (3.2): the must-cover subtopic filter and the home-page
  lines. The check stays a rule about headings, not a list of names.
- **"The new product-name field must be a nullable database column"**: no column is involved. A tool page's body,
  envelope included, is stored as JSON text (`BodyJson`), so the new field is a JSON property; pages written
  before it have none, and `Revise` and the slug read it as `productName ?? title`.
- **Spelling:** the business is "Geek @ Your Spot" (Jeff, 2026-10-07: that is the LLC's name). The plan had it the
  other way, from the config's `PublisherName`, which says "Geek At Your Spot"; see section 5.

### 3.5 Unit 7: each call carries only its own passages

**Findings that shape it** (measured from the run log; token figures are characters over the logged
characters-per-token, good to about 10%):

- **Tool pages carry the whole research block three times per body call, and twice in the opening call.**
  Verified in the log: `=== QUOTEABLE RESEARCH` appears 3 times in each Ramp body call (258,257 characters, 54,680
  tokens) and 2 times in its opening call; a pillar call carries it once. A tool call is 54% research. The cause
  is `GccGenerateService.cs:827`: `BuildBriefAndResearchBlock(create)` feeds the tool branch's `sourceContext`
  (`:865`), which is printed as the publisher block (`ContentPromptBuilder.cs:2789`) and again in
  `Tool summary:` (`:2796`), beside the intended copy (`:2829`). No guard reads the two extra copies, so
  removing them loses nothing. The summary copy also leaks into the fallback SoftwareApplication JSON-LD.
- **The unit of selection is the retrieved chunk, which is already rendered text.** The 105 "pages" are about
  160 chunks (32 per partner). The labels (`Section:`, `Target Entity Match:`) exist only inside the strings;
  only 2 of Stampli's 32 chunks carry a `Target Entity Match`, so the partner must come from the page host
  (`GccRequiredToolMentions.HostKeyOf`), as `GccPartnerToolSlices` does. The block renders only the first 6
  chunks of each page (`GccResearchCaps`), so only those may be offered: a seventh chunk is not in the guard's
  number evidence.
- **The block repeats itself:** 140 of 156 chunks print the parent text (`Context:`) and then the child
  (`Specific detail:`) that is already inside it, 30% of the block; about 25% of paragraph characters are exact
  duplicates. A separate lever; see the questions.
- **Cost, at gpt-4o list prices:** a pillar or blog page costs $0.42 today, about 70% of it evidence. Selection
  beats a cached full block only when the selected share is below half; at the recommended budget a page costs
  about $0.25 (-40%). Tool pages after the dedupe alone: $1.62 to $1.11 across the five. The whole run, $2.47 to
  about $1.61.

**7a. Tool pages carry the research once** (small, safe, no behaviour change). The tool branch's
`sourceContext` becomes the brief fields, must-mention and audience without the research (a new
`BuildBriefOnlyBlock`); `BuildBriefAndResearchBlock` stays for the image-prompt and generic paths. Test: one
`=== QUOTEABLE RESEARCH` in every tool request, the numbered quotable spans still on batch 0 only, the tool
summary free of research.

**7b. Per-slot selection for pillar and blog.**

1. **A facet on each slot** (`SectionSlot.Facet`: Problem, Mechanics, Decisions, Rollout, Outcomes, Boundary),
   declared with the slot that owns the obligation in `PillarPrompts` and `BlogPrompts`.
2. **`GccEvidenceFacets`**: about 25 plain words per facet, in code. **`GccChunkLabels`** beside `RenderChunk`
   reads the labels back, so writer and reader share the constants.
3. **`GccEvidenceSelector`** (pure, deterministic, no network, no model): lexical scoring of each chunk's label
   and text against the slot's `Covers` text plus its facet words (the keyword at half weight; `Guidance` is
   excluded because it carries operator framing). Scores are rounded to 6 decimals and ties break by position,
   so the same input always gives the same selection. Per call: the opening call gets a small budget; a body call
   gets `24,000 + 8,000 per partner` characters (64,000 for five, about 13K tokens, 35% of today's); half of it
   is reserved as a per-partner floor (at least 2 chunks, 1 in the opening), so no call is starved of a partner;
   no partner takes more than 40% when there are three or more; a chunk an earlier call of the same page already
   took is discounted, not banned. If the floors alone exceed the budget they are kept and the event says so.
   When everything fits, everything is shown. The output keeps document order, so it is a verbatim subsequence of
   today's block.
4. **A per-call `evidence` run-log event** (batch, passages, characters, per-partner, whether the floor
   exceeded the budget); `Kind` is free text, so no migration.
5. **Prompt order keeps caching:** the stable text (shared evidence blocks, the partner instruction, the amounts
   note, the research header and rules) first, then the selected passages, then the valid-values list for what
   this call was shown, then the rest. About 5,000 characters of stable text land after the varying part, about
   1,000 uncached tokens a call, not worth changing builder signatures.
6. **Wiring:** `GenerateSectionsInBatchesAsync` takes an optional batch-evidence object and sets
   `batchCtx.EvidenceBlock` per batch (null means today's behaviour, so no builder signature changes and
   `SystemPromptIsStaticTests` is unaffected). The string the guards read is **not** touched: links, numbers,
   `evidence:` tags and quote candidates are still judged against the whole retrieved set, so a link, figure or
   tag from a passage the call was not shown is not refused. The valid-values list names only what the call was
   shown (the writer is told to tag from "the passage as it gives it").
7. **Not selected, and why:** tool pages (after 7a the one remaining copy is cached on batches 1 and 2, so
   selecting it is cost-neutral, and batch 0 must stay whole because the quotable spans are cut from it); the
   publisher positions, competitor, already-published and amounts blocks, about 49K characters (selecting them
   would break the shared prefix and weaken the page-wide "do not write these again"); `Revise`, which carries
   no evidence today, a gap that predates this.

**Tests.** `GccEvidenceSelectorTests` (pure): same input same order; every selected chunk is one the full block
renders, and a page's seventh chunk is never offered; document order kept; a cost slot prefers the cost passage
and a mechanics slot the mechanics one; every partner keeps its floor whatever the slot; the budget caps the
selection but never drops a partner; no partner exceeds its share; research that fits is shown whole; an
earlier call's chunk is demoted but returns when it is far the best match; ties break by position; an unlabelled
chunk is still selectable; every pillar and blog slot declares a facet; `www.bill.com` and `bill.com` are one
partner; the label reader reads what `RenderChunk` wrote; with no signal the fill falls back to document order
and is never empty. `GccBatchEvidenceReachesTheCallTests` through the real pillar and blog paths: the call for
sections 3 and 4 carries different passages from sections 1 and 2; everything before the passages marker is
byte-identical across the calls of a page; every partner is in every body call; the opening is shown fewer
passages than a body call; a link, a figure and a tag from a passage the call was not shown are not refused; the
valid-values list names only what the call was shown; the guards read the whole research text. Added to
`SystemPromptIsStaticTests`: the system message is the same whatever passages the call is shown.

**Measured on the next real run** (Jeff reads the `call` and `evidence` events): pillar opening call 46,078
tokens to at most 17K; body batch 0 53.6K to at most 31K; batches 1 and 2 at most 30K with `cachedTokens` at least
13K; research per body call 188,024 characters to 55-75K, per opening call to 20-28K; one research header per
tool call, not three; pillar and blog page cost at most $0.27; tool page at most $0.25. Safety: no new `links`,
`numbers` or `heading-provenance` findings, no more `partner-mentions` gaps, every partner with at least 5K
characters in every body call, and batch word counts not below today's (pillar 658 / 604 / 473; tool 556-884),
because a drop would mean the writer is starved.

**Questions for Jeff** (section 5, "Still to ask").

## 4. Verification

- `dotnet test GeekBackend.Tests` after each unit, with the new tests shown to fail on the old code first.
- Then one real Generate with at least pillar, blog, tool and one short-form type. Jeff reads the events
  (assistants do not query the database):
  `SELECT at, kind, piece, payload_json FROM content_creator.gcc_generate_job_events WHERE at >= '<run start>' ORDER BY job_id, seq;`
- After unit 2: no `partner-subset` finding anywhere; a missing partner still shows as a `partner-mentions` gap.
- After unit 3: with any refused type, the job ends `ready`, a `settled` event precedes `completed`, "Not
  written (N)" lists the refusals once each, and the other pages exist. No `failure` event unless nothing wrote.
- After unit 4: "Top 5 ... Tools:" absent from the must-cover block and the profile text in the `call` payloads;
  "In every pillar Tools section" and "newspaper-style" absent from every prompt; no `links` finding on an opening.
- After unit 5: every page ends with the fixed line and the questions, linked to `#consultationAppointment2xl`,
  and no `questions-quiz` finding from a closing.
- The model's behaviour under the new wording is the one thing tests cannot show. If a piece is still refused,
  the run log has the draft and the exact sentence, and now it costs one page, not the run.

## 5. Decisions

**Made by Jeff, 2026-10-07**

- **One failure does not kill the batch:** section 3.1.
- **The partner rule is removed**, whole, with nothing in its place (3.2).
- **Positioning text:** "Geek @ Your Spot is an AI implementation consultancy for small businesses located in
  West Palm Beach, Broward and Miami-Dade counties." It drops "B2B organizations", the Tools-section order and the
  Apex, LWC and Agentforce examples. Written exactly as Jeff wrote it, with "@": the business is "Geek @ Your
  Spot". Every other place the name is written as "Geek At Your Spot" is listed in section 5, "Still to ask".
- **The home page's "Top Tools for X" lines are removed** from what the writer reads; the tools come from the
  brief form.
- **The consultant appendix's "Voice" line is removed.**
- **`plans/10-07-26-Logging` and the other untracked plans stay out of commits.**
- **The closing** is "Answer these questions when booking your free consultation", the questions one per line,
  linking to `#consultationAppointment2xl`, on every content type (3.3).
- **Tools are written within the context of the keyword.**

**Default taken, until Jeff says otherwise**

- **Grounding refusals stay whole-run.** A missing partner crawl refuses every type before any model call and
  after delete-first; a test (`AMissingCrawlRefusesEveryTypeEvenOneThatCitesNothing`) pins that over-refusal.
  Making it per type is a separate decision.
- **The page, not the writer, builds the closing** (3.3), because the wording is fixed.

**Made by Jeff, later the same day**

- **The tool page title becomes "{Tool name}: {keyword}"** (yes: otherwise he would have to edit every title by
  hand). Unit 6 below.
- **The closing is built by the page, as a literal statement, on every long-form content type** (pillar, blog,
  tool), so the writer cannot mishandle it.

**Done in the working tree, 2026-10-07 (Jeff: pages already written keep the old name until generated again)**

- `PublisherName` is "Geek @ Your Spot", `AuthorName` "Geek @ Your Spot Editorial Team", and
  `ImplementerPositioning` is Jeff's sentence, in `appsettings.json` and the defaults in
  `ContentGenerationOrchestrator.cs`. The name is not used as a slug, URL or pattern anywhere in `GeekAPI`; the
  full suite passes (1,885). Not touched: `DefaultClientName` in `WorkflowServiceRegistration.cs` (a client
  record, not a page) and the tests, whose contexts set their own names. This is the "positioning" row of the
  3.2 table and the first part of unit 4; it is its own commit, "the business name and positioning".

**Still to ask (unit 7)**

1. **Budgets:** body calls 24,000 plus 8,000 characters per partner (64,000 for five, about 13K tokens), opening
   calls 12,000 plus 2,400 per partner, half of each as a per-partner floor. More evidence per call is safer but
   each extra 10,000 characters costs about $0.012 per uncached call. Acceptable?
2. **Tool pages:** approve the dedupe (7a, a defect, no behaviour change) and leave their research whole?
3. **Licensing every chunk's `Section:` title as an `evidence:` value.** The guard keeps only the first chunk's
   title per page, but the writer is told to copy "the section title as the passage gives it". Widening it
   never creates a failure, but it touches a guard, so it needs your yes.
4. **Facet words** in code (about 25 per facet), or in config?
5. **Print each distinct paragraph once per call** (removes the repeated parent text, 20-30% of the selected
   block, no evidence lost, but the chunks read differently to the model): same unit or the next?
6. **PARTNER DATA on tool pages:** `featureInventory` is 60-90% of it (14-47K characters) on every call. Select
   by section in a later unit?
7. **Reuse discount** 0.5 spreads evidence across a page's calls; 1.0 repeats the best passage wherever it fits.
8. **The valid-values list** names only what the call was shown (recommended), or the page-wide list?

**Answered, kept for the record**

1. **The tool page title.** "Automated Approval Workflows" in Jeff's example stands for the project's keyword,
   so the pattern is "{Tool name}: {keyword}" with the keyword taken from the topic without its department
   ("Accounts Payable: Automated Approval Workflows" gives "Ramp: Automated Approval Workflows"; another
   project's keyword gives another title). Today the stored title is only the product name ("Ramp"). Change it?
   It needs a separate product-name field so `Revise` and the slug keep working, and it changes the exported
   page title; the URL stays `/tools/.../ramp`.
2. **The closing, by the page or by the writer** (3.3). Jeff's answer so far explains the questions, not who
   builds the closing: they are the brief's "One question per line" field, repeatable and different per project,
   about the Methodology (business objectives, data quality assessment, choosing the right AI technologies); the
   page reads that field, so nothing about them is hard-coded. Default: the page builds the closing.

## 6. Not changed, and why

- **The other guards.** `questions-quiz`, `links`, `heading-provenance`, `unlisted-tools` and `partner-mentions`
  keep their severities. A quiz-phrase false positive ("if the answers", on a benign sentence) would still refuse
  a page; this fix makes the cost of a refusal one page, not the run.
- **Delete-first.** It stays first, as decided.
- **The pasted fix from another AI.** Four of its claims were checked and refuted: "Visit our site" came from the
  social post, which no guard reads, and no code refuses that phrase (the quiz check was probably what it
  meant); the FAQ and "never questions" are in separate calls and no check fails on a question heading;
  "105 partner records" is 105 retrieved pages for five partners; and its code does not fit the repo (wrong
  signature for `Instruction`; a 3-word link limit against the real 12; methods and a `PublisherUrl` property
  that do not exist; "the downstream pipeline blends in the other partners", which nothing does).
- **The V2 prompts.**

## 7. Seen in the log, not part of this fix

Say if you want any of these taken up.

- **Cost.** Each pillar and blog body call carries the whole QUOTEABLE RESEARCH block: 105 passages for all
  five partners, about 188,000 characters (roughly 47,000 tokens), every call, whatever the sections are.
  Choosing passages per call would cut most of the cost. Each `call` event also stores 100 to 290 KB, so one run
  is a 23 MB dump.
- **Length.** Every batch came back near 60% of its word floor (pillar 2,632 words against 3,000; tool pages
  2,100 to 2,400 against 3,000) with 1,000 to 1,400 completion tokens against a 16,384 ceiling. These ship as
  warnings, as designed.
- **Mislabelled block.** The block headed "...'S OWN SITE -- USE THIS, DO NOT INVENT AROUND IT" also carries the
  operator's brief notes, the consultant appendix and the must-cover block, because they are appended to the
  crawled paragraphs; the writer is told operator text is published publisher copy.
- **Tool pages have no unlisted-tools check.** The Bill page named Tipalti, AvidXchange and Coupa; the tool prompt
  permits "sibling platforms ... when a real contrast helps".
- **Tool lede is labelled "pillar".** `BuildArticleLedePrompt` says "a schema.org TechnicalArticle pillar" for a
  tool page.
- **Short-form.** The social and email prompts promise a destination URL "injected by the app", which the Create
  path does not do, and print the raw `book_now`; their `outcome` events report `words: 0`.
- **The questions list** holds thirteen entries as entered; five read like article titles in curly quotes
  ("Can invoice software read every line—or just the total?"). Say if those five belong there.
- **Keyword headings.** The rule that a heading carry the exact keyword produced "The Costs of Manual Automated
  Approval Workflows".
- **Cross-type single points.** The two repository reads in `WithMissingToolPagesNamedAsync` (advisory) and a hub
  push that throws after the save can still fail a run whose pieces were saved.
- **Frontend.** `content-creator-v2` has four uncommitted files, including the Run log panel.
