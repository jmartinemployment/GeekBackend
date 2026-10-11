# Research classification: plan

Written 2026-10-11. **Status: approved by Jeff on 2026-10-11 ("execute"). Stage 1 is built. Stages 2 to
5 are not.** What was built, and how it differs from what is written below, is under "Progress" at the
end.

## What it is

Today, every request that writes part of a pillar or a blog carries all of the research: every
passage found on every partner's site. Classification gives each passage a label saying which part
of the page it is for. Each request then carries only the passages for the sections it is writing.

**Yes, it reduces the payload size.** That is its purpose.

## What it would change

| | Now | With classification |
|---|---|---|
| Research in one pillar request | about 488,000 characters | about 195,000 characters |
| Size of the whole request | about 116,000 of OpenAI's 128,000 | about 58,000 of 128,000 |
| Room to spare | about 9% | about 55% |

The "now" figures are worked out from the 18:26 run of 2026-10-10 with that morning's fix applied.
None of the figures is from a request that has run.

**Cost, corrected.** I said classification would make each request cost much less. That was
incomplete. OpenAI charges half price for text it has just been sent, and a page's five requests
repeat the same research. On the 18:26 run, 60% of everything sent was billed at that half price.
Classification gives each request different research, so that discount is lost. At the size above,
the pillar's and the blog's requests still cost about a third less. Above about 60% of today's
size, classification would cost more than it saves.

**Not changed.** The tool pages. Each one carries a single partner's research, fits with room to
spare, and needs all of it in its first request to choose its quotation.

## The labels

Seven, matched to what the pillar's and the blog's sections are about:

| Label | Pillar sections it serves | Blog sections it serves |
|---|---|---|
| The problem and what it costs | opening, what is going wrong, what the status quo costs | opening, what doing it by hand costs |
| How it works | how the approach works | how the work changes once automated |
| Getting it running | what makes an implementation hold up, early decisions, rolling it out | what makes an implementation hold up |
| Data and integrations | the data and the integration | |
| People and roles | the people whose work changes | |
| Results and proof | | what the evidence shows |
| Fit, price and limits | when it is the right call, what to do next | when it is the right call, what to do next |

A passage can carry more than one label. A passage with no clear label is "general" and is shared
among all the requests.

## How a passage gets its label

By code, the same way every time, from three things:

1. **The kind of page it came from.** The search index already marks every page as pricing,
   documentation, blog, product, comparison and so on. It sends that with every passage, and the
   application ignores it today.
2. **Which of your brief's questions found it.** Each partner is searched with the problem and the
   evidence rows you wrote. The application throws away which search found which passage. It would
   keep it.
3. **The words in the passage and its heading,** matched against a short word list for each label.

**Not by a model.** A model would label more accurately, for about a cent a page. It would also be
one more request that can fail, with no second attempt, and its labels would differ from run to
run. If the word matching proves too crude in stage 2 below, this is the thing to revisit.

## What each request then carries

- The passages labelled for its sections, best matches first.
- **Every partner, always.** Each partner keeps a guaranteed share, because the page has to name
  every partner and needs something to say about each.
- A passage an earlier request of the same page already carried counts for less, so the page as a
  whole sees more of the research.
- Up to a limit of 40,000 characters for each partner, and 240,000 in all.
- **When all the research fits under the limit, it is sent whole,** as today. A small project is
  not changed and keeps its half-price discount.

The checks on a finished page (figures, money, links) go on reading all of the research. A page is
never refused because a request was shown less.

## Parallel development: how it is built without touching your runs

Each stage is one commit. Stages 1 and 2 change nothing about what a writer is sent, so they can go
live between your runs while you keep working.

1. **Keep the facts.** Record which search found each passage and the kind of page it came from.
2. **Classify and select in "watch" mode.** On every Generate, the code works out what each request
   would have carried and writes it to the run log: the size, how many passages from each partner,
   and what was left out. The writer still gets everything. Your pages do not change.
3. **Read your real runs.** From one or two run logs: are the passages chosen for each section the
   right ones, does every partner have enough, are the sizes as predicted? Adjust the word lists
   and the limit from what the logs show.
4. **Switch it on for the blog,** on your word. It is the smaller page, three requests. Your run
   judges it.
5. **Switch it on for the pillar,** on your word.

## What can go wrong

- **A section is shown too little** and comes out thinner or more generic. This is the main risk.
  Watch mode, the guaranteed share for each partner and the "page under its word floor" line in the
  gap list are what would catch it.
- **Word matching is crude.** Twice on 2026-10-10 it failed at a harder job, finding the one
  passage that answers a question. Classification needs less: which broad part of the page a
  passage belongs to. That is still unproven until stage 3.
- **The research may not hold what some sections need.** It was searched for with your brief's
  problems and solutions, so little of it may be about rollout, data or people. Classification
  cannot find what was never retrieved. Stage 2 will show how many passages each label gets. If
  some labels are nearly empty, the answer is to search for those sections, which is a separate
  plan.

## What I need from you

Approval of this plan, or changes to it. One figure is yours to set if you want to: how much each
request carries. The plan uses about 40% of today's size.

## How it will be checked

1. Tests for each stage: the same input always gives the same selection, every partner keeps its
   share, the limit is never passed, small research is sent whole, the page's checks still read all
   of the research.
2. Stage 2 on a real run: the run log lists, for every request, what it would have carried.
3. Stages 4 and 5 on a real run: the request sizes in the run log, the gap list against the run
   before, and the page's word counts by section.

## For the code

- **This replaces an earlier design that was put on hold.**
  `plans/fix-the-2026-10-07-generate-run.md`, section 3.5, designed selection for each request on
  2026-10-07 and stopped at eight open questions. This plan answers them itself: the limit above,
  tool pages left whole, no change to any check, word lists in code, a passage identical to its
  section already printed once (`b135a67`), the tool pages' partner data left for later, and an
  already-shown passage counted at half.
- **What is lost today.** `GccGroundingResolver` (the search loop, about lines 535 to 615) knows
  which question found each page and drops it. `HttpGeekCrawlerRagClient`'s `ChunkDto` does not
  read the `category` the index returns (`Geek-Crawler-Rag`, `metadata.py`, `infer_category`).
- **What would be new.** A label on `SectionSlot`, set in `PillarPrompts` and `BlogPrompts`; a
  classifier and a selector, both plain code with no network and no model; an `evidence` event on
  the run log; `BuildResearchBlock` able to print a selection; `GenerateSectionsInBatchesAsync`
  giving each request its own block. `GccResearchCaps` already limits a page to its first six
  passages, and selection would choose among those.
- **What stays whole in every request.** The publisher's own positions, the competitor blocks, the
  "already published on this site" block, the list of valid heading sources and the note on
  amounts not in US dollars.
- **Three sentences in today's prompts assume the whole research is present** and would be
  reworded when a request carries a selection: "find the evidence for each name in QUOTEABLE
  RESEARCH", rule 5 of the research block, and the valid heading sources.
- **A fourth sentence, since 2026-10-11.** The pillar's and the blog's body calls are now told which
  partner answers which failure (`GccPartnerAnswers`), and that block says to look "in that partner's
  passages in QUOTEABLE RESEARCH". It is about 1,260 tokens a call on this project, inside the part
  that stays whole in every request, so the "now" figures above are about 1% higher than written.
  It also gives selection something it lacked: each of a partner's rows names the failure its search
  was for, which is a label by partner and failure that needs no word list.
- **The half-price figure.** 18:26 run, job `5ef1165c`: 1,934,358 tokens sent, 1,169,536 billed as
  cached. On each tool page, requests two to five of the body were about 97% cached. The pillar's
  body requests are ordered the same way (`ContentPromptBuilder.Pillar.cs`, the research before
  anything that differs between requests); they did not run that evening, so theirs is not measured.

## Progress

**Stage 1, keep the facts: built 2026-10-11.** Nothing a writer is sent changed.

- Each page a search returns keeps the kind of page the index marked it (`GccQuoteablePage.Category`:
  pricing, docs, blog, product, about, careers, legal, compare, or page). The index sent it on every
  result and it was not read.
- Each passage keeps which of the brief's searches found it (`GccQuoteablePage.Searches` and `FoundBy`,
  read through `SearchesThatFound`): the brief's problem, one of its rows with the failure entered on
  it, or the keyword. A passage two searches found names both. It is written where the results come
  back (`GccGroundingResolver.FoundBy`) and kept in step through the merge (`WithLaterPassages`).
- It is kept on the page, not in one list for the whole research as first sketched: a page an
  operator uploaded is kept from run to run, and a list held beside the pages could be replaced under
  it.
- The run log's `grounding` event gains `poolSearches` (each search, the failure it was for, and the
  pages and passages it found) and `pageKinds` (how many pages of each kind).
- Checked by test: the research block a writer is sent is the same to the character with and without
  any of this (`No_writer_is_shown_what_found_a_passage_or_the_kind_of_page_it_is_on`).

**What a real run will show for stage 1:** the `grounding` event of the next Generate lists
`poolSearches` and `pageKinds`. That is the first sight of how the research divides by the brief's
questions and by kind of page, and it is what stage 2's word lists are checked against.
