# Research classification: plan

Written 2026-10-11. **Status: approved by Jeff on 2026-10-11 ("execute"). Stages 1 and 2 are built.
Stage 3 waits for a real run. Stages 4 and 5, which change what a writer is sent, are not built
and wait for Jeff's word.** What was built, and how it differs from what is written below, is under
"Progress" at the end.

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

**Stage 2, classify and select in "watch" mode: built 2026-10-11.** Nothing a writer is sent changed.
Every pillar and blog body request still carries all of the research.

- **The labels** are the seven above (`GccResearchLabel`). Each pillar and blog section names the one
  it is written from (`SectionSlot.ResearchLabel`, set in `PillarPrompts` and `BlogPrompts` as the
  table above says). A tool page's sections name none.
- **How a passage gets its labels** (`GccPassageClassifier`). Each of the three things adds to a
  label's strength, and a label needs a strength of 2:
  - the kind of page: a pricing or a comparison page gives "fit, price and limits" 2; a product page
    gives "how it works" 1; a documentation page gives "how it works", "getting it running" and "data
    and integrations" 1 each; a blog post, an about page and an unmarked page give nothing;
  - the searches that found it: the brief's problem gives "the problem" 2; a row's search gives "how
    it works" 2, and each other label the remedy's own words name 1;
  - its own words: 1 for each of a label's words it holds, up to 3. One common word alone gives no
    label.
  The word lists name no subject (no "invoice", no "approval"), and a test fails if one does. The line
  of link text printed under a passage is not read: it is often the site's menu.
- **What a request would carry** (`GccResearchSelector`), in this order: research under 240,000
  characters is carried whole; otherwise each partner has the same limit, 40,000 or its equal part of
  240,000; within a partner, passages labelled for the request's sections first, strongest first,
  then general ones; a passage an earlier request of the same page carried counts half each time; a
  partner left with less than a quarter of its limit is made up to that from its other passages, the
  search's best first.
- **Decided while building, not in the plan above:**
  - a partner's guaranteed share is a quarter of its limit. The plan said "a guaranteed share" and
    named no figure;
  - a passage labelled only for other sections is left out unless it is needed for that share. The
    plan's table assumed about 195,000 characters a request; with this rule a request carries less
    than its limit when a partner has few passages for its sections. The run log will say how much;
  - partners are told apart by host, the way a tool page's research already is
    (`GccRequiredToolMentions.HostKeyOf`). A partner's page on another host of its own (a help or a
    documentation subdomain) counts as a separate partner. If a real run shows one, that is fixed
    before stage 4.
- **On the run log, for every pillar and blog:**
  - `evidence-labels`, once: how many passages carry each label, in all and partner by partner. This
    is what shows whether a label has anything to choose from.
  - `evidence`, once for each body request: the sections' labels, what was sent, what would be
    carried, the same partner by partner (labelled, general, share, left out), and each passage chosen
    and left out with its labels. When the research fits whole it says `whole` and lists nothing.
- **Checked by test:** the same passages give the same choice; every partner is kept and none passes
  its limit; the limit in all is never passed; research that fits is carried whole; a passage is
  counted at what the research block prints for it; and, through the real pillar and blog writers
  with more research than the limit, every body request is still sent every passage and is told no
  label.

**Stage 3 needs one or two real runs.** What to read from them:

1. `grounding`: `poolSearches` and `pageKinds`. Do the brief's rows each bring back passages, and what
   kinds of page are they on?
2. `evidence-labels`: is any label nearly empty? An empty one means the research holds little for
   those sections, and the answer is to search for them, which is a separate plan.
3. `evidence`: on a project over 240,000 characters, how much would each request carry, does every
   partner have enough, and are the passages chosen for a section the right ones? A project under
   that size says `whole` on every request and shows nothing to adjust: the accounts receivable
   project is the one that is over.

**Not built:** stages 4 and 5. Nothing reads a selection into a prompt. The four sentences listed under
"For the code" that assume the whole research is present are unchanged, and are reworded in stage 4.
