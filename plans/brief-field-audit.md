# Audit: is every Brief field used?

Written 2026-10-10. **Status: run the same day; Jeff's answers acted on the same day, below. The
result as it was found follows; the plan it followed is under "The plan as written".** One step is
not done: the comparison with a real run, which needs a Run log.

## What was done with the result (2026-10-10)

Jeff's answers to the nine gaps, and what was done. Nothing here has been seen in a real run, and
nothing is committed or pushed.

| Gap | His answer | Done |
|---|---|---|
| 1. Revise uses none of the brief | "Remove/comment out Revise functionality" | Deleted in GeekAPI and content-creator-v2: the route, the method, the panel, the two "Fix these and revise" buttons. In git at `c972ac2` and `43b7fc8` |
| 2. The core problem reaches no pillar call | Agreed, before the re-run | The pillar's opening call prints its slot's guidance, which is the operator's whole framing |
| 3. The tool page's FAQ and summaries get no brief | Agreed, before the re-run | Both FAQ calls and the summaries call print the reader and the voice, with a line saying the brief is not a source |
| 4. Writing notes missing from seven places | "Remove/comment out Writing Notes functionality" | The field is deleted from the form and from GeekAPI |
| 5. Email and social get none of the framing | "Yes ... They are also marketing material" | Both are given the framing in a short piece's wording: build on one failure, leave the rest |
| 6. Choices arrive as stored codes | "Does it really matter? ... If you can't confirm this than items are addressed after a creation re-run" | Not confirmed, not changed. After the re-run |
| 7, 8, 9 | After the re-run | Not changed |
| The call to action type and label | "Remove/comment out CTA fields. They may have a purpose after the backfill" | Deleted from the form and from GeekAPI, which no longer requires `ctaType`. Values saved before stay in `gcc_project_revisions` |

`BriefFieldReachTests` is re-recorded: the three removed fields are out of it, and the lines for
gaps 2, 3 and 5 changed with the code. One `# GAP` line is left, gap 8.

A brief saved from the form after this no longer carries `ctaType`, `ctaLabel` or `writingNotes`.

**Order to deploy:** GeekBackend first, or both together. The old GeekAPI refuses a brief with no
`ctaType`, so the new form against the old API cannot Generate. The old form against the new API
shows a Revise button that answers 404.

**Found on the way, and kept on Jeff's word** (2026-10-10: *"Keep. We have many disabled Content
Types that we may want to utilize repurpose"*): `GccController.RepurposeInternal` has no caller (its
route has answered 403 since 2026-09-22), and `GenerateToolAsync` and `GenerateRepurposePackAsync`
are called only from it. Its blog and technical-article branches went with Revise, which wrote them:
what is kept makes the social pack, emails, image prompts and tool pages.

## Result, as found before any of the above

### How it was measured

`GeekBackend.Tests/ContentCreator/BriefFieldReachTests.cs`. One field of a complete brief is changed
at a time and each page is written again through the path the project's Generate takes, with a
provider that records what it is sent and always answers the same. A call whose prompt changed is a
call that field reaches. The saved page is compared the same way, which shows what code puts on the
page without a model. A control writes each page twice from one brief and requires the same prompts
to the character.

A changed value was used, not a search for the field's text, because a choice never arrives as
typed. The test stays as the standing check: 16 tests, and the whole suite passes at 1,806.

Not measured by the test, read in the code instead: Revise, the image prompt added to an email or a
social post, the address a page is exported under.

### Field by call

"Body" is every body call unless a call is named. Pillar and tool page have five body calls, the
blog three.

| Field | Pillar | Blog | Tool page | Email, social |
|---|---|---|---|---|
| `primaryIntent`, `secondaryIntent`, `buyingStage`, `audienceSegment`, `audienceNotes`, `toneOfVoice` | opening, body, People Also Ask, title | title, opening, body, FAQ | opening, body | yes |
| `angle` | opening, body, title | title, opening, body | opening, body | yes |
| `writingNotes` | opening, body, People Also Ask | opening, body, FAQ | opening, body | **no** |
| `eeatSignals` | as writing notes | as writing notes | as writing notes | no |
| `ctaType`, `ctaLabel` | withheld | withheld | withheld | yes |
| `paaQuestions` | answered in People Also Ask; body, as headings it may use | body, as headings it may use | no | no |
| `blogFaqQuestions` | no | answered in FAQ | no | no |
| `diagnosisQuestions` | closing, by code | closing, by code | closing, by code | no |
| `taxonomyPath`, first level | page address | page address | page address, and links to it | no |
| `taxonomyPath`, later levels | **no** | **no** | **no** | no |
| category `coreProblem` | **no** | first body call | first body call, when the tool has none of its own | **no** |
| category `painPoints` | first body call | first body call | first body call, added to the tool's | **no** |
| category `automationToPitch` | second body call | first and second body call | first body call, when the tool has none of its own | **no** |
| tool's own `coreProblem`, `painPoints`, `automationToPitch` | no | no | first body call | no |
| tool's own `faqQuestions` | no | no | answered in the operator's FAQ | no |
| `evidence` rows: `solution`, `terms` | no | no | no | no |
| category `faqQuestions`, `lengthBand`, `briefVersion` | no | no | no | no |

**The searches of a partner's crawl** read four things and nothing else: the core problem (the
tool's own, else the category's), each row's `solution` (its `problem` when it has no solution,
`GccNicheFraming.cs:691`), each row's `terms`, and the tool's `faqQuestions`.

**Calls that are given no brief field at all:**

| Call | Where |
|---|---|
| Revise, every call | `ReviseAsync` takes the page, the feedback and the type (`GccGenerateService.cs:1068`, called at `GccController.cs:667`) |
| Tool page FAQ, both calls | `ContentPromptBuilder.cs:1923`, `:2728`. They get the questions and nothing else |
| Tool page summaries (hero, home, blog and advertising summaries, meta description) | `ContentPromptBuilder.cs:2815`, called at `GccGenerateService.cs:1925` |
| Image prompts for a page's sections | `ContentPromptBuilder.cs:2361` |
| The image prompt added to an email or social post | the brief is passed as null, `GccGenerationCoordinator.cs:1015` |

**The checks.** Every figure typed anywhere in the brief is allowed on the page: the brief's text is
evidence for the figure check (`GccGenerateService.cs:3571`). The keyword remap and the linker read
no brief field.

### Gaps, most serious first

1. **Revise uses none of the brief.** A revised section is rewritten without the audience, intent,
   tone, writing notes or the operator's framing. Cause: the method has no brief to read.
2. **The category's core problem reaches no pillar call.** Cause: `PillarPrompts.Outline` puts the
   whole framing on the opening slot (`PillarPrompts.cs:115`); the pillar's opening is written by
   the lede call, which is handed that slot's label and not its guidance (`:147`). The blog's and
   the tool page's opening slot is their first body section, so they do carry it. Why nothing
   caught it: `NicheFramingReachesTheWriterTests` reads the tool body prompt only. `AGENTS.md` said
   the pillar's opening carries the whole framing; it now says it does not.
3. **The tool page's FAQ and its summaries are written without the brief.** No audience, intent,
   stage, tone or writing notes. The summaries are published text.
4. **Writing notes are missing from seven places:** email, social, the title call of the pillar and
   the blog (title, summary, meta description), both tool FAQ calls, the tool summaries, Revise.
   `BuildAudience` (`GccGenerateService.cs:2540`) has no writing-notes line.
5. **Email and social are given none of the operator's framing.** They are told to pitch one idea
   and not told what the operator's idea is.
6. **Every choice reaches the writer as its stored code.** "Audience: in_market", "Primary intent:
   commercial_investigation", "Buying stage: action", and on email and social "Call to action:
   book_now". Only the angle is put into words everywhere. The audience codes are Google Ads
   audience categories (`affinity`, `your_data`, `custom`), so a body call reads "WHO THIS IS FOR:
   your_data" (`ContentPromptBuilder.cs:1524`). The CTA code is what the long-form writer once took
   for a button label.
7. **The hook is written without the operator's problem.** On the blog and the tool page the
   framing is on the first body section, not on the opening call. May be intended.
8. **Only the first level of the taxonomy path is read**, as the department in the page's address
   (`GccContentPath.cs:54`). A first level that is not one of the five departments is ignored
   without a word and the page is filed under the project's department, or `marketing`.
9. **The brief is printed two or three times in one prompt**, in different wordings (three in the
   tool page's opening: `BuildAudience`, `BuildBriefFieldsBlock`, the body guidance). The same
   shape as the tool page's research printed three times, fixed in `eda5b00`.

### Withheld on a decision, to confirm or reverse

| Field | Withheld from | Decision |
|---|---|---|
| `ctaType`, `ctaLabel` | the writer of a pillar, blog or tool page | Jeff, 2026-10-07: the closing is built by code. So on those three pages the operator's call to action and its label change nothing; the closing is the consultation line from `appsettings.json`. `ctaType` is still required before any page is written (`GccGenerateService.cs:182`) |
| `diagnosisQuestions` | every writer | Jeff, 2026-10-07: code puts them in the closing |
| rows' `solution` and `terms` | every writer | 2026-10-08: search input, never quoted |
| `lengthBand` | everything | the form saves it empty and the project route strips it |
| category `coreProblem`, `automationToPitch` | a tool page whose tool has its own | the tool's own replaces them; pain points and rows are added |
| `paaQuestions`, `blogFaqQuestions`, a tool's `faqQuestions` | each other's page | each page answers its own |

### Left over, and read as live by the next person

- `lengthBand`: three places still print it if it is ever present (`GccGenerateService.cs:268`,
  `ContentPromptBuilder.cs:1455`, `:1544`).
- Category `faqQuestions`: saved, always empty, read by nothing.
- `eeatSignals`: always all four (`brief-catalog.ts:478`), so it is a fixed line and not a choice,
  and it is still required (`GccGenerateService.cs:191`).
- `audienceDetails`, `audienceModifiers`: read by the backend, and no form field writes them.
- `hierarchyPlan.recommendedTools`: read from the brief by `GccRequiredToolMentions` (`:33`, `:108`,
  `:171`); the form never writes it.
- The older field names are mapped twice, once in the frontend and once in
  `ExtractBriefFields`, whose comment points at the retired `GeekContentCreator` repository
  (`GccGenerateService.cs:2281`). Whether any stored brief still carries an old name is a database
  question and is not checked.

### Not done

The comparison with one real run of each page type. It needs a Run log.

## The plan as written

## Context

Jeff, 2026-10-10: "Incorporate and use everything from Brief. Sounds like an audit of the each Brief
field needs to be planned."

What prompted it: the tool page's FAQ. Asked whether a supplied answer should be printed as typed, he
said it "may not be in the same Tone of Voice, & etc brief fields the affect output." Checking that
showed the tool page's FAQ is not given those fields today (see "Already known").

The audit answers one question for each field: where does what the operator entered change what is
written, searched or checked, and where does it not?

## What "used" means

A field is used by a call when its value reaches that call's prompt, or decides something in code (a
search, a check, a line built by code). A field that is read into an object and goes no further is not
used. A field withheld from a call on a recorded decision is listed as withheld, with the decision,
so it can be confirmed or reversed. It is not counted as a gap and not counted as used.

## The fields

From the one place that defines them, `content-creator-v2/src/lib/content-creator/brief-catalog.ts:185`.

| Group | Fields |
|---|---|
| Intent | `primaryIntent`, `secondaryIntent`, `buyingStage` |
| Audience | `audienceSegment`, `audienceNotes` |
| Shape | `angle`, `lengthBand` |
| Voice | `toneOfVoice`, `eeatSignals`, `writingNotes` |
| Call to action | `ctaType`, `ctaLabel` |
| Questions | `paaQuestions`, `blogFaqQuestions` |
| Niche framing, category | `taxonomyPath`, `diagnosisQuestions`, `coreProblem`, `painPoints`, `automationToPitch`, `evidence[]` (`problem`, `solution`, `terms`), `faqQuestions` |
| Niche framing, per tool | `perTool[host]`: `coreProblem`, `painPoints`, `automationToPitch`, `evidence[]`, `faqQuestions` |
| Bookkeeping | `briefVersion` |

The reader also accepts older names (`audiencePrimary`, `audienceDetails`, `audienceModifiers`,
`audienceDetail`, `audienceExclude`, `intent`, and older `buyingStage` and `ctaType` values;
`GccGenerateService.cs`, `ExtractBriefFields`). Each is audited as an alias: does anything still send
it, and does it reach the same places as the current name.

**Not in this audit unless Jeff says:** what a project holds beside the brief (topic, notes,
department, site section, partner and competitor URLs, uploaded research).

## The calls each field is checked against

| Page | Calls |
|---|---|
| Pillar | title and outline, opening, each body call, People Also Ask, closing (built by code), Revise, image prompts |
| Blog | title and outline, opening, each body call, FAQ, closing, Revise, image prompts |
| Tool page | opening, each body call, partner FAQ, operator FAQ, closing, Revise, image prompts |
| Short form | email, social, ads |
| Not a model call | the searches of each crawl, the page checks (`GccDraftGuard`), the keyword remap, the linker |

## Method

**1. Trace by the saved key, not by the C# name.** A first count by property name is misleading: it
shows `diagnosisQuestions` and `taxonomyPath` on two lines each, but a key can be read under another
name. For each key: where the operator enters it and what its label promises, where the frontend
saves it, where GeekAPI reads it, and every place that value goes from there.

**2. Follow every route a brief value can take.** Found so far, each to be confirmed complete:

| Route | Where |
|---|---|
| The brief's body guidance | `ContentPromptBuilder.BuildBriefBodyGuidance` (`:1503`) |
| The research brief | `ResearchBriefBuilder.Build` |
| The brief as a block of text | `BuildBriefOnlyBlock`, `BuildBriefAndResearchBlock`, `BuildAudience` in `GccGenerateService` |
| The niche framing | `GccNicheFramingReader` and the framing's own guidance methods |
| The searches | `GccGroundingResolver.PartnerQuestions`, `FaqQuestions` |
| Lines built by code | `GccClosing`, `GccRequiredToolMentions` |
| The checks | the brief's text is evidence for the figure check (`GuardInputsFor`) |

**3. Prove it with a marked brief, not by reading alone.** A test fills every field with a value that
appears nowhere else, runs each page through its real generate path with a provider that records what
it is sent, and prints which field reached which call. A choice field (tone of voice, intent) is
checked by the words that choice produces. Reading the code says where a value should go; this shows
where it went.

- The pillar and the tool page have fixtures that record prompts
  (`GccGenerateServicePillarFaqTests`, `GccGenerateServiceToolPageGroundingTests`).
- No fixture writes a whole blog. One is built as part of the audit.

**4. Check one real run.** A run's record holds every prompt as sent (`call` events). The marked-brief
result is compared with one real run of each page type, so the audit does not rest on test fixtures
alone. This needs a run's Run log from Jeff; the database is not reachable from this session.

## What the audit delivers

- **One table: field by call.** Each cell is reaches, does not reach, or withheld, with a file and line.
- **A list of gaps**, most serious first, each with what the operator entered and what ignored it.
- **A list of fields withheld on purpose**, each with the decision and its date, for Jeff to confirm
  or reverse. Two are already recorded in `AGENTS.md`: the writer of a pillar, blog or tool page is
  not given the CTA setting or the consultation questions, because the closing is built by code.
- **The marked-brief test, kept.** Once the gaps are settled it becomes the check that fails when a
  field stops reaching a call it should, or a new field is added and wired to nothing.

No fix is made during the audit. The fixes are planned from its result.

## Already known

Verified in the code on 2026-10-10. These are the audit's first rows, not its result.

- **The tool page's FAQ is written without the brief's voice.** `BuildBriefBodyGuidance` carries tone
  of voice, audience, intent, buying stage, E-E-A-T signals, length band and writing notes. The pillar's
  and blog's FAQ prompt adds it (`ContentPromptBuilder.cs:1882`). Neither tool FAQ prompt does:
  `BuildToolFaqFromQuestionsPrompt` (`:1923`-`:1961`) and `BuildToolFaqSectionPrompt`
  (`:2728`-`:2767`). Both set voice from the brand tone and the publisher's positioning only.
- **A tool's FAQ questions were never searched for.** Built, uncommitted:
  `plans/search-the-crawl-for-faq-questions.md`.
- **The category's `faqQuestions` is always empty.** The frontend saves a tool FAQ only per tool
  (`brief-catalog.test.ts:203`).
- **A tool FAQ answer is not a field.** The box takes one question per line. Jeff's direction if it
  becomes one: the answer is reworded by the writer under the brief's voice, not printed as typed.

## Size

About twenty-seven fields against about twenty calls. Reading and the marked-brief test are one
session's work. The blog fixture is the largest single piece.
