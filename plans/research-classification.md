# Research classification: plan

Written 2026-10-11. **Status: every stage is built and pushed. Approved by Jeff on 2026-10-11
("execute"); stages 4 and 5, which change what a writer is sent, were built on 2026-10-12 on his word
("Research classification: finish this off"). What is left is not code: no project large enough to
be chosen from has run, so the first one that does is the first sight of the choice on real
research.** What was built, and how it differs from what is written below, is under "Progress" at
the end.

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

Changed on 2026-10-11 from the first real run; what it was and why it changed is under "Progress",
stage 3.

- **Every partner, always, up to the same limit:** 40,000 characters for each partner, and 240,000 in
  all (five partners are 200,000; with seven or more each has an equal part of the 240,000). The page
  has to name every partner and needs something to say about each.
- **Within a partner, the passages labelled for the request's sections come first,** best matches
  first, then passages with no label, then the partner's other passages until its limit is reached.
  Labels decide the order, not whether a section is shown enough.
- A passage an earlier request of the same page already carried counts for less, so the page as a
  whole sees more of the research.
- **Research of 350,000 characters or less is sent whole,** as today. A project that fits is not
  changed and keeps its half-price discount.

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
   judges it. *Built 2026-10-12.*
5. **Switch it on for the pillar,** on your word. *Built 2026-10-12, on the same word as stage 4, so
   one run judges both.*

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
- **What a request would carry** (`GccResearchSelector`), as "What each request then carries" above
  says. As first built it was different in two ways, both changed in stage 3: research was whole only
  under 240,000 characters, and a passage labelled only for other sections was left out unless its
  partner had less than a quarter of its limit.
- **Decided while building, not in the plan above:** partners are told apart by host, the way a tool
  page's research already is (`GccRequiredToolMentions.HostKeyOf`). A partner's page on another host
  of its own (a help or a documentation subdomain) counts as a separate partner. The first real run
  had none: five partners, five hosts. If a run shows one, that is fixed before stage 4.
- **On the run log, for every pillar and blog:**
  - `evidence-labels`, once: how many passages carry each label, in all and partner by partner. This
    is what shows whether a label has anything to choose from.
  - `evidence`, once for each body request: the sections' labels, what was sent, what would be
    carried, the same partner by partner (labelled, general, other, left out), and each passage chosen
    and left out with its labels. When the research fits whole it says `whole` and lists nothing.
- **Checked by test:** the same passages give the same choice; every partner is kept and none passes
  its limit; the limit in all is never passed; research that fits is carried whole; a partner with
  nothing for a request is carried up to its limit all the same; a passage is counted at what the
  research block prints for it; and, through the real pillar and blog writers with more research
  than is carried whole, every body request is still sent every passage and is told no label.

**Stage 3, read the real runs: the first was read on 2026-10-11, and the rules changed from it.**
Still "watch" mode: nothing a writer is sent changed.

The run: 14:20 UTC, project "Cash Flow Forecasting: Automated Spreadsheet Data", job `101c3590`. Seven
pages written, none refused.

What its log showed:

| | |
|---|---|
| Searches | 30: the brief's problem once for each of five partners, and its 25 rows. Every one brought back passages (40 and 197) |
| Pages, by the kind the index marked | 127: 68 with no kind, 23 blog, 21 product, 8 comparison, 6 pricing, 1 about |
| Research in a pillar or blog request | 213 passages, 250,717 characters |
| A pillar body request | 68,328 tokens, 76,520 of 128,000 with its reply's reservation |
| Passages by label | the problem 67, how it works 186, getting it running 8, data and integrations 47, people and roles 48, results and proof 7, fit, price and limits 47, no label 0 |

What each request would have carried under the rules as first built:

| Request | Sections | Characters, of 250,717 |
|---|---|---|
| Pillar 1 of 5, blog 1 of 3 | the problem | 102,089 |
| Pillar 2 of 5, blog 2 of 3 | how it works, getting it running | 182,742 |
| Pillar 3 of 5 | getting it running, twice | 54,630 |
| Pillar 4 of 5 | data and integrations, people and roles | 132,693 |
| Pillar 5 of 5 | fit, price and limits, twice | 91,059 |
| Blog 3 of 3 | results and proof, fit, price and limits | 100,550 |

Four things were wrong, and each is changed:

1. **A section the research holds little for would have been shown a fifth of it.** No passage was
   without a label: every one was found by the brief's problem or by one of its rows, and each of
   those gives a label. So "then general ones" never applied, and the pillar's third request was 8
   passages about getting it running and 33 make-weight ones. **Changed:** a partner is carried up
   to its limit in every request, its passages for the request's sections first, then its others.
   This is the 195,000 characters the table at the top of this plan assumed.
2. **On this project, choosing would have cost more to send less.** The log measured the half-price
   discount for the first time on a pillar: of about 68,300 tokens in each body request, 66,432
   were billed at half price on the second to the fifth. A request with its own choice of research
   loses that. Chosen down to three quarters, as the 240,000 line would have made it, the pillar
   would have cost about 30% more. And it was nowhere near the model's limit. **Changed:** research
   of 350,000 characters or less is carried whole. At that size a request is about 97,000 of
   128,000. Just above it, with five partners, a request carries 200,000: about 5% more to send for
   a pillar, about 5% less for a blog, and two thirds the size.
3. **One word was counted as two.** "client" and "clients", "team" and "finance team", "plan" and
   "plans" each counted twice, so one common word gave a label. **Changed:** a word and its other
   forms are one word, and a test fails if a phrase in one line holds a word from another.
4. **Some words are the subject's own.** In cash flow forecasting a plan is a financial plan and a
   timeline is a forecast's: 17 passages were labelled "fit, price and limits" by "plan" alone.
   Seven passages were labelled "results and proof" by "results", "review", "faster" and
   "increased"; five were read and none was a customer's result. **Changed:** those words are out
   of the lists.

**What the run showed that this plan cannot fix.** The research holds almost nothing for some
sections. Of the eight passages labelled "getting it running", seven were read and about half are
really about setting a product up; of the seven labelled "results and proof", the five read were
not a customer's result. The pillar's three sections on getting it running and the blog's section
on results are written with next to no evidence of their own today, with or without classification. The plan
foresaw it ("What can go wrong", third point). The answer is to search for those sections, which
is a separate plan and is not started.

**What is still needed.** This project is under the ceiling, so its next runs will say `whole` on
every request and show nothing to judge. The choice is only exercised by a project over 350,000
characters. The accounts receivable project is the one known: about 488,000, whose pillar and blog
requests are about 117,000 of 128,000 and have not run since they went over on 2026-10-10. Its
`evidence` events are what say whether the passages chosen for each section are the right ones.

**Stages 4 and 5, switched on for the blog and then the pillar: built 2026-10-12**, on Jeff's word
("Research classification: finish this off"). One commit each, and one before them for the partner
fix the progress above said had to come first.

- **A partner is the project's partner whose site a page is on or under**
  (`GccResearchSelector.PartnerOf`). Told apart by host alone, "help.partner.com" was a partner of
  its own with its own limit. Of two partners a host is under, the longer one.
- **What is sent.** When a page's partner research is over 350,000 characters, its opening and each
  of its body requests carry the passages chosen for the sections that request writes and no others
  (`GccResearchPerRequest`, printed by `BuildResearchBlock(create, carried)`). Under the ceiling
  every request is sent all of it, built exactly as before.
- **The opening is the page's first request.** This plan spoke of body requests. The opening
  carries the research too and is as near the model's limit as a body request, so leaving it whole
  would have left the page's largest request where it was. It is chosen for the problem, and what it
  carried counts at half for the body's first request.
- **What stays whole in every request,** as "For the code" listed: the publisher's positions, the
  competitor blocks, the site's own coverage, the heading sources and the note on amounts not in
  dollars; and the partners to name and what each partner answers.
- **The four sentences that assume the whole research is present are not reworded.** Read again,
  each already says what to do when a partner has no passage ("named plainly with no claims
  attached", "claim nothing more"), and every partner is carried in every request. One line is
  added to a block that is a part (`GccGenerateService.ResearchIsAPartNote`): the passages are the
  ones chosen for the request's sections, and a passage that is not there is not evidence that a
  product lacks something.
- **The checks on the finished page read all of the research,** as this plan said: the guard's
  inputs are built from the whole evidence, not from what a request carried.
- **The record.** `evidence`, once for the opening (request 0) and once for each body request: what
  the research holds, what the request carried, the same partner by partner, each passage chosen
  and left out. Nothing records a choice without sending it any more, so "watch" mode and its field
  are gone.
- **Checked by test,** through the real pillar and blog writers: over the ceiling every request
  carries each passage its record lists as chosen, none it lists as left out, and the line saying it
  is a part; between them a page's requests reach more of the research than any one carries; under
  the ceiling every request carries every passage and nothing says it is a part; no label is
  printed.

**Size, worked out and not measured.** From the 18:26 run of 2026-10-10 with the fixes since: the
accounts receivable project's research is about 488,000 characters and a body request about
117,600 of 128,000 with its reply's reservation. With 200,000 characters carried (five partners at
40,000) a body request is about 61,000 of 128,000, and an opening about 45,000 where it was about
100,000. The requests no longer repeat one block of research, so the half-price billing for repeated
text is lost; at this size the page still costs about a fifth less to send.

**What is left, and it is a run, not code.** The choice has never been made on real research over
the ceiling: both projects run since 2026-10-11 are under it and are sent everything, as before.
The accounts receivable project is the one known to be over. On its next run, read from the run log:
the size of each `call`, each `evidence` event's passages chosen and left out against the sections
that request wrote, the page's words by section, and the gap list against the run before. The main
risk is the one this plan named first: a section shown too little.

**Still not in this plan:** the research holds almost nothing for the sections on getting it
running and on results. Choosing cannot find what was never searched for.
