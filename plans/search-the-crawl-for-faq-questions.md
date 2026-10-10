# A tool page's FAQ questions are never searched for

Written 2026-10-10. **Status: built the same day on Jeff's word, all seven changes. Not committed,
not pushed, not yet seen in a real Generate.** 1,790 unit and 44 integration tests pass. The cause
below is read from the code. Which path dropped the one quoted question is still not verified; the
run's record decides it.

A review of this plan was pasted the same day. It agrees with the plan and raises one risk, answered
under "The review's one risk, checked".

**Found while building, and not in the plan or the review.** The page's figure check refuses a page
that states a figure found in none of its evidence (`GccDraftGuard.AddNumberFindings`), and its
evidence was the page-level block. An FAQ answer written from its own search's passages, carrying a
figure only those passages have, would have refused the whole tool page. The passages found for a
tool's questions are now part of the evidence that check reads (`toolFaqEvidenceText`,
`GccGenerateService.cs`), pinned by
`AFigureAnAnswerTakesFromItsOwnPassageIsNotRefusedAsUnsupported`, which failed before the change.

**Not done, in the other repository.** The brief page lists, per tool, the searches that partner's
crawl gets (`content-creator-v2/src/components/content-creator/NicheFramingPanel.tsx:182`, built by
`partnerQuestions` in `src/lib/content-creator/brief-catalog.ts:372`). It does not list the FAQ
searches, and `brief-catalog.test.ts:206` says a tool's FAQ questions leave the searches unchanged.
That list is now short by one search per FAQ question. Found after the build, on checking the
frontend; not started.

**Not covered by a test:** the blog's unanswered-question report. It goes through the same function
as the pillar's, which is tested; no test fixture writes a whole blog.

## Context

A pasted review item says FAQ questions such as "How do customer risk segments influence the cash
forecast?" are flagged as left out, and blames `PrepareBodyInput` and `MaxPeopleAlsoAskQuestions = 12`.

Those two names are the Workflow product (`ContentGenerationOrchestrator.cs:18`, `:1648`). That code
reports nothing about a missing question. The only place that writes "it was left out" is the Content
Creator's tool page: `GccGenerateService.cs:1764`, "FAQ: no page of {name}'s answers "{question}"; it
was left out." The Content Creator has no cap of 12; it sends questions eight to a call.

## The cause, verified in the code

**The operator's FAQ questions for a tool are never sent to that partner's crawl.**

- The crawl is searched in `GccGroundingResolver.PartnerQuestions` (`:679`): the brief's core problem,
  then one search per evidence row. It does not read `faqQuestions`.
- `faqQuestions` has exactly one reader, `GccGenerateService.cs:1621`, and it runs after every search
  is over.
- The FAQ call is told to answer only from the partner's passages. Those are the passages the other
  searches happened to bring back. A "page" here is the pieces of a page a search returned
  (`HttpGeekCrawlerRagClient.MapChunksToQuoteable`), not the whole page.
- So a question about something none of those searches was about has no passage. The writer leaves it
  out, as told. The report then says no page of the partner's answers it. Nothing looked.

**Not verified: that this is what dropped the quoted question in the run.** The same message appears
two other ways, and only that run's record separates them:

- `GccToolFaqEvidence.BuildFor` pools up to eight questions and keeps the 24 paragraphs that match
  the most words across all of them. One question can get none.
- `AnswersQuestion` (`:3192`) keeps an answer only when its heading is the question, word for word or
  one inside the other. A reworded heading throws the answer away under the same message.

The run could not be read while writing this: the Supabase account connected to the session does not
hold GeekRepository's database. The Run log for that tool's FAQ call shows the passages sent and the
reply.

## Why nothing caught it

- The FAQ fields and the brief-driven search were both built on 2026-10-08 and were never joined. One
  reads the brief for what to search, the other for what to answer.
- The 2026-10-09 fix changed which part of the already-fetched passages the FAQ call sees. Its comment
  says a new search was avoided because no safe run id exists at that point. The resolver has the run
  id for each partner while it is searching (`partnerHostKeyByRun`, `:471`).
- The tests hand the FAQ code pages that already contain the answer. No test asks whether a question
  was ever searched for.
- The message states a conclusion, and the run's `grounding` event records counts, not what each
  partner was asked (`GccGenerationCoordinator.GroundingRecord`).

## The review's one risk, checked

The review asks that a search which finds nothing return an empty list, not null.

- **It already does.** `GeekCrawlerRagQueryResult.Pages` is a required list
  (`HttpGeekCrawlerRagClient.cs:162`), and `MapChunksToQuoteable` returns an empty one when no
  passage comes back (`:852`). A null result is read as the library failing and refuses the run
  (`GccGroundingResolver.cs:518`). The loop already reads `result.Pages.Count` on every search.
- **The null the plan had not covered is its own new list.** It is optional, so research that never
  went through the search has none. A question with no entry was not searched, which is a different
  fact from "searched, found nothing", and must not be reported as it. Change 2 now writes an entry
  for every question searched, and change 5 has a row for a question with no entry.
- **That case is reachable.** The search runs in one place, `GccGenerationCoordinator.cs:110`. One
  caller writes a tool page without it: the Workflow product (`ContentGenerationOrchestrator.cs:383`).
  It passes no create, so it has no questions at all.
  - **Corrected 2026-10-10.** This also named `GccController.cs:857`, tool pages derived from a draft
    by `aiToolNames`. That line is inside `RepurposeInternal`, which is private and has no caller: its
    route has answered 403 since 2026-09-22. It is not a reachable caller.
- **No throw is added.** A failed search is a returned refusal (`GccGroundingOutcome.Refuse`).
  Everything else is a line in the draft's warnings.

Two things the review says that the code does not: the discard at `GccGroundingResolver.cs:542` is not
"baseline since 2026-10-07" (that date is when the passage work was held), and it removes passages
that differ, not copies. Its recommendation to leave it out is the plan's.

## What changes

**1. One reader of a tool's FAQ questions** — `GccNicheFraming.cs`.
`GccNicheFramingReader.FaqQuestionsForHost(briefJson, host)`. `ToolFaqQuestions` (`:239`) resolves
the product to its host and calls it. The search and the writer then read the same list.

**2. Each question is searched for in its own partner's crawl** — `GccGroundingResolver.cs`.
In the partner loop, when the run writes tool pages (`contentTypes` in `ResolveAsync`, `:259`), one
search per FAQ question for that host, shaped like the core problem's search. Three passages a
question, so a call of eight carries at most the 24 it carries today.

- Results go in their own list, by host and question. They do not join the shared pool, so the
  pillar, the blog and the tool body are given exactly what they are given now.
- A failed search refuses the run the way a failed evidence search already does (`:518`-`:530`). A
  search that finds nothing is not a failure.
- Every question searched gets an entry, with no passages when the search found nothing. No entry
  means not searched.

**3. Carried to the tool page** — `GccResearchModels.cs`, `GccGenerationCoordinator.cs:41`,
`GccPartnerToolSlices.cs`.
A new optional list on `GccResearchDocument` and `GccGroundingOutcome`. `MergeRetrievedEvidence` sets
it fresh each run. `GccPartnerToolSlice.Narrow` keeps only its own host's entries.
`MergeRetrievedEvidence` returns early when every list is empty (`:43`); the new list joins that
test, so entries that all found nothing are still carried.

**4. The FAQ call reads those passages** — `ToolFaqAsync`, `GccGenerateService.cs:1727`, and
`GccToolFaqEvidence.cs`.

- A question whose search found nothing is not sent to the model.
- The rest go eight to a call, each shown with the passages found for it.
- The word-matching selector in `GccToolFaqEvidence` is deleted. It becomes the renderer of these
  passages. Two ways of finding a question's evidence is how they drift.

**5. The report says what was checked** — `GccGenerateService.cs:1764`, `:3200`.

| What happened | What the operator is told |
|---|---|
| The search found nothing | a search of {host}'s crawl found nothing for "{question}"; it was left out |
| Passages were shown, no answer came back | the writer was shown {n} passage(s) from {host} for "{question}" and did not answer it |
| An answer came back under a heading that is none of the questions | the writer answered under "{heading}", which is not a question it was sent; the answer was dropped |
| The question has no entry | {host}'s crawl was not searched for "{question}"; it was left out |

A question with no entry is not sent to the model either.

`NormalizeQuestion` treats any run of whitespace as one space. Today a tab or a non-breaking space is
deleted, which joins two words.

**6. The run record** — `GccGenerationCoordinator.GroundingRecord`, `ToolFaqAsync`.
The `grounding` event lists each FAQ search with its host and how many passages it found. A new `faq`
event lists each question and which of the outcomes above it met.

**7. The pillar and the blog, the path the paste names** — `WriteFaqInBatchesAsync`,
`GccGenerateService.cs:3155`.
A call that answers seven of eight questions loses one without a word. Its comment says a missing
answer is a refusal; the code refuses only when a call answers none. Each question with no answer
under its own words is named in the draft's warnings, through the list the word floor already uses
(`Writers/GccGenerateService.Pillar.cs:169`): "no answer came back under "{question}"". An answer
under a heading that is none of the questions is named too, and stays on the page as it does today.
What ships does not change; only what is reported. The comment is corrected. This part stands alone
and can be struck.

## Not changed

- No second model call, and no answering from general knowledge.
- No prompt for the opening or the body. No change to the passages any other writer sees.
- The Workflow product's FAQ and its cap of 12.
- **Found here, fixed 2026-10-10 on Jeff's word ("address outstanding items"):** `GccGroundingResolver`
  discarded a later search's passages when an earlier search had already returned something from the
  same URL, which thinned the evidence for every writer. A later question now adds its passages to the
  page (`WithLaterPassages`).

## Cost

One more search per FAQ question per partner. A search is not a model call. Fewer model tokens when
a question finds nothing, because it is no longer sent.

## Tests

- **The cause, pinned:** every FAQ question a tool page will be asked is among the searches its
  partner's crawl was given, with the right run id, and no other partner's. None when the run writes
  no tool page. In `GccGroundingEvidenceQuestionsTests`.
- The shared pool is the same with and without FAQ questions (`GccGroundingRetrievalTests`).
- `Narrow` keeps only its host's entries (`GccPartnerToolSlicesTests`).
- `GccToolFaqEvidenceTests` rewritten for the renderer; the pooled-matching tests go with the code.
- `GccGenerateServiceToolPageGroundingTests`: each row of the table in change 5; a question with no
  passages reaches no model call; a passage found for a question reaches its call when the shared
  pool holds nothing on it.
- `GccGenerateServicePillarFaqTests`: seven of eight ships and names the eighth; none still refuses.
- Whitespace in a question: two spaces, a tab, a non-breaking space.
- A search that finds nothing leaves an entry with no passages, and the merge carries it when every
  other list is empty (`GccGroundingResolverTests`, the coordinator's merge tests).
- A question with no entry reaches no model call and is reported as not searched, never as found
  nothing.

## Verification

1. `dotnet test GeekBackend.Tests` and the integration tests, all passing (1,739 and 44 today).
2. A real run of a tool page whose brief has FAQ questions. In its Run log, the `grounding` event
   lists each question with a passage count, and the `faq` event gives each question's outcome. The
   quoted question is either answered or named with its real reason.

## Order

Changes 1 to 6 are one unit: none is useful alone. Change 7 is its own. Both go on
`cleanup/one-content-creator`, which already holds four changes no real run has seen. Nothing is
committed or pushed until Jeff says. `AGENTS.md` gets one bullet under "Content Creator pages" when
the code is in.
