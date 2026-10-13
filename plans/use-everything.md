# Use everything: the Brief, the research, and Repurpose for the short types

**Status:** approved 2026-10-12 ("execute your plan"). Steps 1, 2 and 3 of six are built and
committed on `main`. Nothing is pushed, and nothing is fixed until a run of yours says so.

## Where the work stands

| Step | | |
|---|---|---|
| 1 | Research Organization's fixed numbers go, requests are counted against what the model accepts, OpenAI's limit figures on the run log | Built, committed, not pushed |
| 2 | The Brief boxes (Part 1) and the hints on the form | Built, committed in both repositories, not pushed |
| 3 | Nothing is cut by a count (Part 2) | Built, committed, not pushed |
| 4 | Repurpose, GeekBackend | Built, committed, not pushed. **Must not be pushed without step 5**: until the form changes, ticking "Email — cold outreach", "Social" or "Ads" on Generate is refused |
| 5 | Repurpose, the form | Not started |
| 6 | The record | Not started. `AGENTS.md`, the specification and the sources document already say what steps 1 to 4 changed |

**Step 1, as built. What differs from what this plan said is marked.**
- Research is no longer cut at 350,000 characters or to 40,000 for each partner. Every opening and
  body request of a pillar, a blog and a tool page is built with all of its partner research,
  counted, and sent whole when it is within what its model accepts with the reply's reservation.
- **The count is OpenAI's own.** Checked against the 40 requests in your saved log of the 08:17 UTC
  run of 2026-10-07, not the 15:29 run as this plan said: I cannot read that run's requests from
  here. The 16 requests with no reply format came to OpenAI's figure exactly, from 245 to 46,078
  tokens. The 24 with a reply format came to it less a fixed 383 or 368, which is the format, and
  OpenAI counts a format its own way. So a count is exact but for a few hundred tokens of format.
  Counting a 370,000-character request takes about 14 thousandths of a second.
- **Over the limit** the room is shared between the partners equally, **and what a partner with
  less research does not use goes to the others**, which this plan did not say. Everything left out
  is on the run log. A request that is over with no partner research in it is refused before it is
  sent, saying its size.
- **A model that is not `gpt-4.1` or `gpt-4o`, or named after one of them, is refused** at the start
  of a run, before its old pages are deleted, and again where a request is sent. **So if GeekAPI's
  `LlmProviders__OpenAi__ExtractionModel` or `LlmProviders__OpenAi__UtilityModel` names any other
  model, every run is refused once this is pushed, with a message naming it.** A run with the
  Anthropic provider is refused for the same reason.
- **The run log.** Each of those requests has an `evidence` line before it is sent with its size
  (`request.tokens`, `withReply`, `accepts`). Every request's `call` line has the size as counted
  (`countedTokens`) beside OpenAI's own (`promptTokens`), and OpenAI's stated allowance
  (`rateLimit`). Nothing acts on the allowance.
- **Moved to step 3:** counting every passage of a page. The count and the block that prints the
  passages have to change together, and the block is step 3's.
- **Not in step 1:** competitor pages giving way first when a request is over. Only five are shown
  until step 3 shows all forty, and the rule is built with them.
- Tests: 2,039 unit and 44 integration pass. A pillar, a blog and a tool page are written through
  their real writers with more research than `gpt-4o` accepts and with the same research on
  `gpt-4.1`.

**Step 2, as built. What differs from what this plan said is marked.**
- **Both problems are searched for.** A partner's site is searched with that tool's "Core pain
  point" and then with "The core problem", eight passages each. The tool's own is first, as it was,
  so the pages it finds keep their place. One statement typed in both boxes is one search.
- **A tool's page is told both problems and both pitches**, each failure with the remedy noted for
  it, in a block every one of its five body requests carries. Its opening section is sent to that
  block, **where this plan said the opening section's own text would print both**: printed there as
  well, the first request would have held the same thing twice. Its hook is told both problems.
- **A failure you entered for every partner and again under a tool is one failure there, with both
  remedies under it.** This plan did not say; nothing is dropped.
- **Rows saved for all partners**: the remedy is listed on the pillar, the blog and every tool
  page, under no tool's name. Only rows with a remedy are listed on the pillar and the blog; a
  failure with none is already what their sections are told to cover.
- **A tool's failures saved without a row** are listed under that tool on the pillar and the blog.
- **A row's failure that no saved paragraph states is argued as a failure.** **Not in this plan:**
  it was argued only when the Brief held no failure paragraphs at all. A Brief saved from the form
  is not affected, because the form saves each row's failure both ways.
- **A row with no failure typed is searched beside the others.** **Not in this plan:** it took the
  place of the first row saved for all partners that had no failure, which the form's own list
  "Searched on ..., in this order" never showed.
- **The check on the Brief screen** searches a partner's site with both problems. If either search
  cannot be made, the partner is reported as not checked.
- **The form.** "Searched on ..., in this order" in a tool's section lists both, each by its box:
  "Core pain point (own)" and "The core problem (category)". The hints under "Core pain point", "The
  automation to pitch — the marketing/sales hook", the rows, and the line under "Per tool" no longer
  say the category's is used only where a tool's box is empty.
- **I was wrong that a page's checks do not read these.** The check that a figure is in the
  evidence has always read the whole Brief as your own statements. That is unchanged. The block is
  not added to what the quotation and link checks read.
- **Size, measured:** the block is 707 tokens for a tool's section filled in as your research fills
  it, in each of a tool page's five body requests: about 17,700 tokens a run of five tool pages.
  The second problem search adds up to eight passages for each partner that has its own pain point.
- Tests: 2,064 unit and 44 integration pass; the form's 55. `BriefFieldReachTests` now fails if a
  box of the category's framing or of a tool's section reads "nothing" on the pillar, the blog or
  the tool page, whatever its table says. I have not looked at the live screen: the form's change
  is not deployed until it is pushed.
- **What the next run lists because of this step:** nothing I can name before it runs. A tool
  page's five body requests each carry your failures and remedies where one did, and each partner
  with its own pain point brings up to eight more passages, so a line may appear or go.

**Step 3, as built. What differs from what this plan said is marked.**
- **Every page a search returned is shown, whole:** every competitor page (five were), every page of
  your own site on the topic (five were), every passage of a page (six were) and every heading
  (eight were).
- **Competitor heading structure** is the outline of each competitor page the search returned, every
  heading, under the same number that page has in the competitor research beside it. The first five
  pages of the first competitor's crawl were shown before, 25 headings each. A returned page with no
  outline is on the run log (`left-out`). **A project whose competitor search returns no page is now
  shown no outline**, and the pillar and the blog are told no competitor heading can be cited.
- **What the question searches found is in the body requests:** every partner's for the pillar and
  the blog, its own partner's for a tool page. **Also in the opening, which this plan did not
  say:** the opening and the body read one list of a page's research, so there is one list and not
  two.
- **Each passage says what it was found for.** The searches are listed once, numbered, in your own
  words ("The problem: ...", "Where it goes wrong: ...", "A remedy noted: ...", "The question:
  ..."), and each passage ends "Found for: 3, 7". **The checks read the same text, where this plan
  said they would read it without those lines:** the check that a figure is in the evidence already
  reads the whole Brief, so a second text would have changed nothing it accepts.
- **A tool page** is handed the competitor research, the competitor outlines (with no heading-tag
  rule, which a tool page does not have) and your site's pages on the topic in every body request,
  and your positions in its hook. A page on a partner's help or documentation address is that
  partner's.
- **Partner data** is read from every passage of a page, whole. **Quotable sentences** are read from
  every page a partner returned, 32 at a time. The list of 40 a tool page may quote from is unchanged.
- **Counts this plan did not list, found while building, and removed:**
  - where a search's results become a page: 80 passages a page, a stop at about 64,000 characters a
    page, a cut of each passage and of the section around it at 2,000 characters, eight link labels
    a passage, 300 characters of a title. The Library's own passage is at most about 700 tokens, so
    the 2,000 cut the end of a full-length section and the others were rarely reached;
  - the list a pillar and a blog are given of what a heading may be tagged with: 40 of each kind.
    Every value is listed, and the competitor kind points at the outlines shown;
  - your home page: its first 24 headings and 40 blocks of text, each cut at 600 characters. Read
    whole;
  - amounts not in US dollars named for the writer: the first 30. Every one is named;
  - product names given to the partner data request: the first 12. Every one is given.
- **Competitor pages give way first** when a request is over what its model accepts: all but five,
  one from each competitor in turn, each with its outline, named on the run log
  (`competitorPagesLeftOut`). Only if the request is still over is partner research chosen from.
- **Left as they are, and why:**
  - "Search results" titles and questions in the research block (12 and 15): nothing writes them.
  - The page-fetch path with its own limits (`GccPartnerUrlResearchService.FetchAsync`): nothing
    calls it.
  - The short "Key site headings" and "Representative site copy" lines in the title-and-outline
    request: a summary beside your home page, which the same request carries whole.
  - Searches still return 8 passages, 3 for a question.
- **Two things to know:**
  - The partner data requests are larger and are not counted before they are sent. A page too long
    for the model fails that page's read, as any failed read does; none is near it (a page's
    passages are a few thousand tokens).
  - A page found only by a question search is not read for partner data or for quotable sentences.
- **Size, measured** through the real writers on research of the 15:29 UTC run's shape with both
  problems searched (364,000 characters of partner research, 150 question passages, 40 competitor
  pages with outlines of 30 headings, 8 of your own pages). Made-up text of the same length, so the
  first real run's `evidence` lines are the real figures:

  | Request | Tokens | With the reply's reservation | Of GPT-4.1's 1,047,576 |
  |---|---|---|---|
  | Pillar or blog, one body request | about 157,000 | about 165,000 | 16% |
  | Pillar or blog, opening | about 127,000 | about 133,000 | 13% |
  | Tool page, one body request | about 61,000 | about 70,000 | 7% |
  | Tool page, opening | about 31,500 | about 33,500 | 3% |

  A run of a pillar, a blog and five tool pages sends about 3.2 million tokens, where the 15:29 run
  sent about 1.75 million. On GPT-4.1's prices that is about $4 if a page's later requests are billed
  as repeats, and about $7 if none are.
- Tests: 2,102 unit and 44 integration pass. A pillar, a blog and a tool page are written through
  their real writers with research past every one of these counts, and
  `NothingIsCutByACountTests` names each count and fails if it comes back.
- **What the next run lists because of this step:** nothing I can name before it runs. Every writer
  is handed about twice what it was, a tool page is handed competitor pages for the first time, and
  an opening is handed what the questions found.

**Step 4, as built. What differs from what this plan said is marked.**
- **A short piece is written by Repurpose, from one approved page, and nowhere else.** A Generate
  asked for one is refused before anything starts: "'social' is written by Repurpose from an
  approved pillar, blog or tool page, not by Generate. Nothing was generated."
- **The route is on the project, beside Generate, which this plan did not say:**
  `POST api/geek-content-creator/projects/{id}/pages/{pageId}/repurpose`, with the pieces to write.
  This plan had the old route start the job. The old route took a version and no project, so it
  could not be held to the project's gates; it is deleted with the writer behind it.
- **The same two gates as Generate, in one piece of code both routes call:** the project's site is
  crawled, and the Brief is complete. A Repurpose and a Generate also share the project's one
  running place, so a Generate cannot delete a page while a piece is being written from it.
- **What it is written from:** a pillar, a blog or a tool page of the project that is approved and
  has text. Anything else is refused with nothing started, in these words: "approve the page before
  Repurpose", "this is a piece written from one", "this page is a ...", "the page has no text to
  write from".
- **What the writer is handed:** the page, whole, and from the Brief what a short piece was already
  handed (the reader, the voice, the problem). **No longer handed, which this plan did not say:**
  the block "This site already covers this topic" (your site's structure matched to the keyword,
  with the subtopics a page must cover). The page was held to it; a piece makes one idea's case.
- **Size, counted as OpenAI counts:** a piece from a 3,000-word page with a complete Brief is about
  3,750 tokens, in made-up text of that length. Each piece is followed by its image prompt request,
  as before. Three pieces from one page cost a few cents on GPT-4.1.
- **The check:** a figure in the piece must be in the page. A piece that states another is refused
  and not written again; the other pieces are kept. When every piece is refused the run fails and
  says why for each.
- **Delete first:** the page's pieces of the types asked for are deleted before a word is written,
  as a Generate deletes its pages.
- **Where a piece is saved:** under its page, carrying the page's name, one of each type for a page.
  A second of the same is refused by an index. Writing the page again, or deleting it, deletes its
  pieces, which the repository already did.
- **The run is recorded like a Generate,** with the page it was written from. A page's newest
  Repurpose is read at `GET .../pages/{pageId}/repurpose/latest`. **The project's newest Generate
  no longer answers with a Repurpose,** so the last Generate's list of gaps stays on the page.
- **Six more types are Repurpose types, which this plan did not list:** `linkedin`, `x`, `instagram`,
  `facebook`, `metaads` and `googleads`. Generate wrote each as its own post; the form offers none of
  them. They are refused on Generate with the three.
- **The export names a piece for its page:** "email/accounts-payable-from-pillar.json". A pillar and
  a blog are both named for the keyword, so their two emails would otherwise be one file name and a
  "-2". Found while building.
- **Deleted:** the old route and its request shapes, the writer that made a pack of posts from a
  page, the writer that made a tool page from a page, and a document serializer nothing else called.
- **The migration** (`APieceIsSavedUnderItsPage`) adds one column to the run table and one index to
  the pages table. It deletes nothing. No row can break the index: every call that has ever created
  a page, in every version of GeekAPI, was read, and none saved a piece under a page.
  **Not run against a real database here** (no Postgres on this machine); checked against the model
  with EF and read as the SQL it generates. It runs when GeekRepository starts after the push.
- **Short pieces saved by earlier Generate runs stay** as pages of the project. No run deletes them
  now, since Generate no longer takes those types. Step 5 looks at how the form shows them.
- Tests: 2,139 unit and 44 integration pass. The route is tested for an approved page, a page not
  approved, a project whose site is not crawled, an incomplete Brief, a page of another project, a
  type that is not a short piece, a piece as the source, a run already going, and the page's newest
  Repurpose read back. A run is tested through the real writers for delete-first, the figure
  refusal with the others kept, every piece refused, and a second Repurpose replacing the first.
- **What the next run lists because of this step:** a Generate lists nothing new. A Repurpose lists
  one line for each piece refused for a figure.

It replaces the earlier plan of the same day (one research block per content type), deleted on
your word: this plan drops its 40,000 for each partner and does not include its rewrite.

## Changed since you last read this plan

- **The model.** GeekAPI restarted at 22:04 UTC on 2026-10-12 with the variable you changed, and is
  up. GPT-4.1 accepts 1,047,576 tokens in a request; GPT-4o accepted 128,000. I have not read the
  variable's value, because listing the variables would show the keys. The first request of your
  next run shows the model on its run log.
- **Nothing is left out for size.** The largest request in this plan is about 165,000 tokens, 16%
  of what GPT-4.1 accepts (measured in step 3; this said 161,000 and 15% when it was worked out).
- **Passages found for the pillar's and the blog's questions now go into their body requests.**
  The only reason they did not was room.
- **Competitor heading structure.** I had written "All shown". It is read from every page of every
  competitor's crawl, not from a search. The plan shows the outlines of the pages the competitor
  search returned. Part 2 says why, and what every page would cost.
- **Order of work.** Research Organization's two fixed numbers go first. Otherwise searching with
  both problems takes this project past 350,000 characters, where today's code cuts every pillar
  and blog request to 200,000.
- **The review you pasted.** Three of its points are now in the plan. Five are not, each with the
  reason. The next section lists them.

## The review, checked against the code

The reviewer knew of GPT-4.1. The numbers it works from (114,000, the 128k mark, 60%) are from the
copy of this plan it was given, which still said 128,000.

**In the plan now**

| The review says | What the code and the run show | What this plan does |
|---|---|---|
| Matching the 40 returned pages to their outlines by address can miss without a word | True that a miss would be silent. Not for relative addresses: both are full page addresses from the same crawl. But a passage carries the page's address after a redirect when it has one (`HttpGeekCrawlerRagClient.cs:863`), and I have not yet read which the outline carries | One comparison that ignores `http`/`https`, `www.`, a trailing slash, `#...` and case. Every returned page with no outline is on the run log, by its address |
| Counting tokens for 65 large requests will lag | A Generate is a background job, and each request is counted on the task that builds it, beside an OpenAI call that takes seconds. GeekAPI runs on Railway, not Hostinger | No new machinery. Measured in step 1: a 370,000-character request takes about 14 thousandths of a second to count, and a test holds it under a tenth of a second |
| Writing a page again silently deletes the pieces under it | True, and Generate says nothing today when it replaces a page. Its premise is not: a piece cannot be edited by hand in the app, so there is no hand-tuned piece to lose | One line under Generate's list when a ticked page has pieces under it: how many are deleted with it. Text, not a panel and not an extra click |

**Not in the plan, and why**

| The review says | Why not |
|---|---|
| Give partners a fair share before competitor outlines take the room | That is already the order: competitor pages give way first, then the partners share equally. On GPT-4.1 a request is at 15%, so it does not arise |
| Build the dry run that reports sizes first, "given how tight the 128k boundary is" | It is not tight any more. Each request's size is on the run log before it is sent, and OpenAI's own figures after |
| Make `SaveGeneratedAsync` upsert with `ON CONFLICT` in Dapper | It is not Dapper. It finds the page by type and name and replaces its content in one write, and a piece under a page is saved the same way. A second of the same is refused by the index, as two pages of one type and name are today. The test for Repurpose twice with the same types is in "Verification" |
| Hold a request, or send it again, when OpenAI refuses it for rate (a 429) | Taken out on your word, 2026-10-12: "are we fix something that won't break?" It has not happened: Railway's log back to 2026-10-05 holds none. GPT-4.1's allowance is the same as GPT-4o's at each account level (450,000, 2,000,000 or 30,000,000 tokens a minute), and the 15:29 run sent about 700,000 in a minute without a refusal, so yours is 2,000,000 or 30,000,000. After this plan the busiest minute is about 2 million, worked out, but the pillar's and blog's requests are over 128,000 tokens each and count against GPT-4.1's separate allowance for those (5,000,000 or 10,000,000), which leaves under 1 million on the ordinary one. The suggestion is filed in `docs/technical-specification.md`, section 15, in case it ever happens. Sending again would also be a retry, and your rule of 2026-10-06 is no retries |

**Rate.** One thing is kept from that point, built with the counter in step 1. OpenAI states the
account's limit and what is left of it on every reply. Those figures go on the run log with each
request, so your next run shows the real numbers for GPT-4.1 in place of my working. A request
OpenAI refuses for rate fails its page as any failed request does today; GeekAPI's log and the run
log already carry OpenAI's reply when that happens.

## Context

Jeff, 2026-10-12: "WHY am I spending so much time entering and you not using", "STOP NOT USING
DATA, EVEN IF THAT MEANS IT COSTS MORE", "NOT USING THE DATA YOU ARE GIVEN IS A MAJOR FLAW", and
short content types "ARE DERIVED OR REPURPOSED FROM EXISTING CONTENT", through the Repurpose path,
which is broken and is to be fixed.

**The cause, and it is mine.** Each box on the Brief and each search result was wired to one use on
the day it was added. Limits were written as counts (the first six passages of a page, the first
five competitor pages), which cut by position. And where a box reached nothing on a page, I recorded
"nothing" in the test table instead of treating it as a failure.

**The rule from here.** Nothing you enter and nothing a search brings back is left out for a count.
The one limit is what the model accepts in one request: 1,047,576 tokens on GPT-4.1. If a request
would ever pass it, Research Organization chooses what that request carries, and the run log lists
what was left out.

The numbers below are from the 15:29 UTC run of 2026-10-12 on "Tax Compliance & Regulations: Complex
and Changing Laws", read from its run log. That run was on GPT-4o.

## Part 1. What you enter on the Brief

| Box | Today | After |
|---|---|---|
| "The core problem" and a tool's "Core pain point" | A partner's site is searched with the tool's own **instead of** the category's | Searched with **both**, eight passages each |
| The same two boxes, on that tool's page | The page is told the tool's own only | Told both |
| "The automation to pitch" and a tool's "The automation to pitch — the marketing/sales hook" | The tool's own replaces the category's | Told both |
| "The tool's remedy" (each row) | Used to search. Never shown to that tool's own page | Shown to the tool's page beside "Where they fail" |
| Everything in a tool's section | Reaches the first of the tool page's ten sections | In all five body requests, so all ten sections |
| "Rows saved for all partners" | The failure is shown. The remedy is shown to no writer | The remedy is shown to the pillar, the blog and every tool page |
| A tool's failures saved without a row (older Briefs) | Reach its tool page only | Reach the pillar and the blog too |
| The check on the Brief screen that asks whether a partner can be quoted | Asks with one of the two problems | Asks with both |

A tool page is told these as your note, as the pillar and blog are today: it argues from them,
quotes none of them and takes no figure from them. The check that a figure is in the evidence
already reads the whole Brief as your own statements, and still does; the quotation and link checks
do not read the note.

Not printed, and why: the later levels of "Taxonomy path". They are the project's topic in other
words, and every request already carries the topic.

## Part 2. What the searches bring back

| Research | Today | After |
|---|---|---|
| Competitor research | Each competitor's site is searched once and gives 8 passages. 40 passages came back, each on a different page: five competitors at 8 each. The first 5 of those 40 pages were shown | All 40 shown |
| Your own site's pages on the topic | 8 came back. 5 were shown | All shown |
| Competitor heading structure | Not from a search: read from every page of every competitor's crawl. The first 5 pages were shown, 25 headings each | The outline of each competitor page the search returned (40 on that run), every heading |
| A page's passages | The first six (at most 13 more existed on that run) | All shown |
| A page's headings | The first eight | All shown |
| Passages found on a partner's site for the questions (30 for each partner) | The FAQ request only | Also in that partner's tool page body |
| The same passages from every partner, for the pillar's and the blog's questions (150 on that run) | The question requests only | Also in the pillar's and the blog's body requests |
| What each passage was found for | Recorded. Shown to no writer | Each passage says which of your failures it was found for |
| Tool page: competitor heading structure | Not handed | Handed, the same 40 outlines |
| Tool page: your positions, in its hook | Not handed | Handed |
| Tool page: pages on a partner's help. or docs. address | Dropped (none on that run) | Kept, by the rule the pillar already uses |
| Partner data | Read from each passage cut at 1,200 characters (a passage averages 1,300) | Read from each passage whole |
| Quotable sentences | Read from the first 32 pages a partner returns | Read from every page |

The five competitors are worked out from 40 passages at 8 each. I have not opened the project to
count them. A tool page is already handed your home page; I was wrong to list it as missing.

**Two things are not "everything", and neither is for room:**
- **Competitor heading structure** is the outlines of the pages the competitor search returned,
  not of every crawled page. Every crawled page is the competitors' whole sites, most of it not on
  the topic. The code that reads it records an earlier project where five competitors were 1,777
  pages (`GccCompetitorAnalysisResolver.cs`). At today's 25 headings a page that is about 200,000
  tokens, more than everything else in a request put together, in each of about 33 body requests.
  It fits GPT-4.1 and would more than double what a run sends. Say if you want every page.
  Today's five are the first five pages of the first competitor's crawl, whatever they are.
- **Quotable sentences** stay a list of 40 for a tool page. They are sentences cut from passages
  the writer is already shown whole. The list is where the page's one quotation may come from.

Searches still return 8 passages each, 3 for a question. That is how much is fetched, not something
fetched and dropped. Raising it is not in this plan.

## The one limit: what the model accepts in one request

**The limit is counted on each request alone:** what is sent, plus the room reserved for the reply.
Nothing adds up across a page or a run. On that run a pillar was 8 requests, a blog 6 and a tool
page 9 or 10, about 65 in all. A pillar's and a blog's body requests carry every partner's research.
A tool page's carry its own partner's. The two are never in one request together.

| Request | Today | After | With the reply's reservation | Of GPT-4.1's 1,047,576 |
|---|---|---|---|---|
| Pillar or blog, one body request | 74,900 tokens | about 153,000 | about 161,000 | 15% |
| Tool page, one body request | 41,300 tokens | about 76,000 | about 84,000 | 8% |

Worked out from the run log before step 3 was built; what step 3 measured is under "Step 3, as
built" above (about 157,000 and 61,000). Not in the sum, because the log does not size them: an
outline's headings past the 25th and a page's headings past the eighth, which add; and passages a
second search returns that the request already has, which are printed once.

Two things still change, because the code as it stands would cut research that fits:

1. **Research Organization's two fixed numbers go.** Today it sends everything up to 350,000
   characters, otherwise 40,000 for each partner. Searching with both problems takes this project's
   partner research from 295,537 characters to as much as 364,000, and past 350,000 today's code
   would cut every pillar and blog request to 200,000.
2. **Every request is counted before it is sent, against what its model accepts.** Today no code
   knows a model's limit; that is how the pillar and blog were refused on 2026-10-10.
   - Everything fits: everything is sent, in the same order in every request of a page. On GPT-4.1
     that is every project run so far.
   - It does not fit: competitor pages beyond the first five give way first, each with its outline
     (step 3, when they are first shown). Then each partner gets an equal share of the room, what a
     partner with less research does not use going to the others, the passages for the request's
     sections first. Every passage or page left out is listed on the run log.
   - Tool pages go through it as the pillar and blog do.
   - Each request's size is on the run log before it is sent.

The reply's reservation stays as it is: 8,192 tokens for a request that writes two sections, 16,384
for one that writes more. GPT-4.1 allows a reply of 32,768.

**Cost.** A run of a pillar, a blog and five tool pages sends nearly twice the tokens it does
today. GPT-4.1 charges $2.00 a million for new text and $0.50 for repeated text; GPT-4o charged
$2.50 and $1.25. Worked out, a run after this plan costs about what that run cost on GPT-4o,
roughly $4. That assumes a tool page's later requests are billed as repeats the way the pillar's
were; I read that from the log for the pillar and the blog only. If none were billed as repeats it
would be about $7.

## Part 3. Email, Social and Ads through Repurpose

**Today.** "Email — cold outreach", "Social" and "Ads" are ticked on Generate and each is written
on its own from the Brief, with no page in front of the writer. Repurpose is switched off, and
behind the switch it is broken six ways: it is given no Brief; it never checks that the project's
site is crawled; its reply is in a shape the form cannot show; it saves under types the form has no
tab for; what it makes is not tied to the page it came from; and no control on the form calls it.

**After. One way to get a short piece: Repurpose, from a page.**

| | |
|---|---|
| Where you ask | On an approved pillar, blog or tool page, under "Approve & export": "Repurpose", with "Email — cold outreach", "Social" and "Ads" to tick |
| On Generate | The three leave the list. A request for one there is refused, with the reason |
| What the writer is handed | The page's own text. From the Brief, what a short piece is handed today: the reader, the voice and the problem. No research: the page already used it |
| The request | A request of its own, made after the page is written and approved. It carries that page whole, a few thousand tokens for a 3,000-word pillar, and none of the passages. Nothing is searched |
| Check | A figure in the piece must be in the page it came from. No second attempt |
| How it runs | As a job, like Generate: the same two gates (the project's site is crawled, the Brief is complete), progress pushed to the page, every request on the run log |
| Where it is saved | Under the page it came from. One of each type for a page; Repurpose again replaces it. Writing the page again, or deleting it, deletes them, which the repository already does |
| What a piece is | Unchanged: an Email is a subject, a body and button text; a Social post or an Ad is its text; each with its image prompt |

Also in this part:
- One page is the source of a piece. A pillar and a blog together would also fit in one request.
  That is not in this plan unless you say so.
- The draft view shows an Email's subject, body and button text, and a post's text, as text. The
  code prints them as raw data today; I look at the screen before I change it.
- Short pieces saved by earlier runs stay until you delete them.
- The old route also made a tool page and an image prompt from a page. A tool page is written from
  its partner's crawl, and "Image prompt" has its own place on Generate, so those two branches are
  deleted with the old writer. On 2026-10-10 you said to keep Repurpose: this keeps it and removes
  those two branches. Say if either should stay.
- Comparison, Alternatives and Listicle, which you named for Repurpose later, are not in this plan.
  The route takes a list of types, so they are added to it, not built beside it.

## Order of work

One commit each, on `main`. Nothing is pushed until you say; a push to GeekBackend is a deploy and
is checked first for a running Generate or crawl.

1. Research Organization's fixed numbers go, requests are counted against what the model accepts,
   and OpenAI's limit figures go on the run log.
2. The Brief boxes (Part 1), and the hint under "Core pain point" on the form.
3. Nothing is cut by a count (Part 2).
4. Repurpose, GeekBackend (Part 3).
5. Repurpose, the form (content-creator-v2).
6. The record: `AGENTS.md`, `docs/technical-specification.md`, `docs/content-creator-sources.md`.

## Where in the code

Paths are under `GeekAPI/Services/ContentCreator/` unless they say otherwise.

**The limit** (step 1, built)
- `Workflow/Providers/OpenAiModelLimits.cs`: what each model accepts: `gpt-4.1` 1,047,576 and
  `gpt-4o` 128,000, matched from the start of the name. In a file of its own, because a test holds
  `ProviderOptions.cs` to naming no model. A model in neither refuses the run before any paid
  request and names the model (`GccGenerateService.ModelRefusal`, `OpenAiProvider`).
- `IContentGenerationProvider.ModelFor`: the model a request would be sent to, so it can be counted
  against that model before it is sent.
- `GccRequestSize.cs`: the token count of a request (system, user and reply format), with
  `Microsoft.ML.Tokenizers` in `GeekAPI.csproj`.
- `GccResearchSelector.cs`: `Select` takes the room and `SharesOf` divides it; the three fixed
  numbers are deleted. Counting every passage of a page (`PassagesOf`) is step 3.
- `GccGenerateService.RequestThatFitsAsync`, in place of the method that chose evidence: builds the
  request with everything, counts it, chooses only when it is over. The pillar's, the blog's and
  the tool page's opening and body requests all go through it.
- Rate: `Workflow/Providers/OpenAiProvider.cs` reads OpenAI's `x-ratelimit-limit-tokens`,
  `x-ratelimit-remaining-tokens` and `x-ratelimit-reset-tokens` from each reply, and
  `GccRecordingProvider` puts them on the `call` event. Nothing else changes: no holding and no
  second send.
- Left as they are: the reply reservations (`ContentPromptBuilder.cs:270-299`) and the size of a
  question request (`GccPageFaqEvidence.cs:46`, which splits questions across requests and leaves
  nothing out). Their comments gave 128,000 as the model's limit and are corrected.

**Part 1** (step 2, built)
- `GccNicheFraming.cs`: the method that merged a tool's section over the category's is deleted. A
  tool's framing is `GccToolFraming`, the category's set and the tool's own, both kept:
  `Problems` (what a site is searched with), `RowsToSearch`, `HookGuidance`. `OwnForHost` is the one
  reader of a tool's own section, in place of three. `GccNicheFraming.Failures` reads a set's
  failures once each with the remedy noted for it.
- `GccGroundingResolver.PartnerQuestions`: one problem search for each statement.
- `GccPartnerAnswers.cs`: `ForToolPage`, the block for one tool's page (both problems, every
  failure with its remedy, both pitches), and `ToolOpeningGuidance`, which sends the opening
  section to it; `Instruction` gains the rows saved for all partners with their remedy, and a
  partner's failures saved without a row.
- `ContentTypes/ToolPrompts.cs`: `Body` adds the block to every body request's evidence, so the
  request that sends the opening section to it is a request that carries it. `GenerateToolPageAsync`
  hands the framing on (`ContentTypePromptContext.ToolFraming`) and builds no block itself.
- `GccAngleQuoteProbe.ProbeAsync` and `GeekAPI/Controllers/ContentCreator/GccController.cs`: the
  Brief screen's check, one search for each problem.
- content-creator-v2 `brief-catalog.ts` (`partnerQuestions`) and `NicheFramingPanel.tsx`: the list
  of a tool's searches and the hints.

**Part 2 (step 3, built)**
- `GccPageResearch.cs` (new): `PagesOf`, the one list of a page's research (what the body searches
  found and what the question searches found, a page once and a passage once), and `SearchesOf`.
  `BuildResearchBlock` and `GccResearchPerRequest` both read it.
- `GccGenerateService.cs`: `BuildResearchBlock` prints every heading and passage, the numbered list
  of searches (`SearchesHeading`, `DescribeSearch`) and each passage's `FoundForLabel` line;
  `BuildOwnSiteCoverageBlock` and `BuildCompetitorResearchBlock` print every page;
  `BuildCompetitorHeadingBlock` prints the outlines of the returned pages;
  `RecordCompetitorPagesWithoutOutlineAsync` puts a returned page with no outline on the run log;
  `RequestThatFitsAsync` tries a request with competitor pages given way before it chooses partner
  research (`GccRequestCarries`, `CompetitorPagesThatGiveWay`). `GenerateToolPageAsync` builds the
  tool page's competitor block and hands its hook the positions.
- `GccCompetitorOutlines.cs` (new): `AddressKey` (the one comparison of two addresses), `For`,
  `WithoutOutline`, `Shown` and `GivenWay` (five kept, one from each competitor in turn).
  `BuildProvenanceEvidence` still reads every outline, so a `competitor:` tag is checked as before.
- `GccCompetitorAnalysisResolver.cs`: one read of a project's competitor crawls for a run, now that
  each tool page asks too.
- `GccHeadingProvenanceGuard.LicensedValues`: every value listed; the competitor kind points at the
  outlines when they are shown.
- `GccResearchSelector.cs`: `PassagesOf` counts every heading and passage and the "Found for" line.
- `GeekCrawler/HttpGeekCrawlerRagClient.cs` (`MapChunksToQuoteable`, `RenderChunk`) and
  `GccGroundingResolver.WithLaterPassages`: no count where search results become a page;
  the class of per-page limits those read is deleted.
- `GccPartnerToolSlices.Bucket` uses `GccResearchSelector.PartnerOf`.
- `Partner/GccPartnerExtractionService.cs` (`BuildUserPrompt`): every heading, passage and product
  name, whole. `GccTypedPassageReader.cs`: every page, 32 a read.
- `GccPublisherProfileResolver.cs`: the home page whole. `Guardrail/GccCurrencyGrammar.cs`
  (`ForeignAmountsInstruction`): every amount.

**Part 3, GeekBackend (step 4, built)**
- `GccRepurpose.cs` (new): which types are short pieces, what a Generate and a Repurpose are refused
  with (`GenerateRefusal`, `TypesRefusal`), the page as a Repurpose reads it or why it cannot be
  written from (`SourceOf`), the page as the writer is shown it (`SourceBlock`), what a piece is
  called (`PieceName`), and the figure check (`FigureRefusal`, on `Guardrail/GccFigureGrammar`).
- `Controllers/ContentCreator/GccProjectsController.cs`: `Repurpose` and `LatestRepurpose`; `GateAsync`
  and `NotStarted`, the two gates and the start's refusals, called by Generate and Repurpose alike.
  `GccController.cs` has no Repurpose route.
- `GccGenerateJobRunner.StartRepurpose`; `GccGenerationCoordinator.RunRepurposeAsync` (delete first,
  each piece on its own, one save under the page); `ValidateRequestedTypes` refuses a short piece;
  `PersistAllAsync` takes the page a run's pieces are saved under.
- `GccGenerateService.cs`: `RepurposeEmailAsync` and `RepurposeSocialPostAsync`, the only writers of
  a short piece.
- `GccArtifactExportService.cs`: `FromPage`, the end of a piece's file name.
- `HttpClients/HttpGccRepository.cs`: `DeletePagePiecesAsync`, `GetLatestRepurposeJobAsync`.
- GeekRepository: `GccGeneratedPiece.ParentPageId`; `GccProjectPageRepository.SaveGeneratedAsync`
  saves a piece under its page and refuses one whose page is not the project's;
  `DeletePiecesAsync`; `GccGenerateJob.SourcePageId` (`source_page_id`), with
  `GccGenerateJobRepository.StartAsync` refusing a page that is not the project's,
  `GetLatestForProjectAsync` answering Generate runs only and `GetLatestForPageAsync`; migration
  `20261012233000_APieceIsSavedUnderItsPage` (the column, and the index
  `ux_gcc_artifacts_one_piece_per_page_type_and_name`, created by SQL like
  `ux_gcc_artifacts_one_page_per_type_and_name`).

**Part 3, the form (step 5, not built)**
- content-creator-v2: `src/lib/content-types.ts` (the three leave Generate's list; their tabs
  stay), `ProjectContentWorkspace.tsx` ("Approve & export", and the line under Generate's list that
  says how many pieces are deleted with a ticked page), `src/services/gcc-api.ts` (the call, and
  `renderArtifactBody`), `src/lib/types.ts` (`RepurposeMixRequest` deleted).

## Verification

- After every commit: `dotnet build GeekAPI`, `dotnet test GeekBackend.Tests` (2,139 after step 4),
  `dotnet test GeekBackend.IntegrationTests` (44); the form's tests (55 after step 2) for its
  commits.
- The counter was checked against the 40 requests of the 08:17 UTC run of 2026-10-07 whose sizes
  OpenAI reported, and one of them is kept as a test. GPT-4o and GPT-4.1 count tokens the same way.
  Your next run checks it again: each request's `call` line carries both counts.
- A test gives Research Organization more than fits and fails if a request passes the limit, or if
  something left out is not on the record. A second gives it a model the table does not hold and
  fails if any request is sent.
- A test gives a reply carrying OpenAI's limit figures and fails if the run log's `call` event
  lacks them.
- The counter's test times a 370,000-character request and prints the time.
- A test returns a competitor page whose address differs from its outline's only by `www.`, a
  trailing slash or case and fails if the outline is missing; another returns a page with no
  outline and fails if the run log does not name it.
- `BriefFieldReachTests` is the record of Part 1. Its tables change in the same commit, and no box of
  a tool's section reads "nothing" on the pillar, the blog or the tool page afterwards, except
  "Search terms", whose use is the search. `GccGroundingEvidenceQuestionsTests` expects both problem
  searches.
- A test writes a pillar, a blog and a tool page through the real writers with more than six
  passages on a page and more than five competitor pages, and fails if one is missing from a request.
  It also fails if the outline of a competitor page no search returned is printed. Built in step 3
  (`BriefFieldReachTests`), with a second that fails if a passage found for a question does not say
  so in an opening or a body request, and `NothingIsCutByACountTests` for each count by name.
- Repurpose: tests through the real route for an approved page, a page not approved, a project with
  no crawled site, a figure the page does not hold, and a second Repurpose replacing the first; the
  repository's tests for saving and deleting a page's pieces. Built in step 4:
  `GccProjectRepurposeRouteTests` (the route), `GccRepurposeRunTests` (a run through the real
  writers, with the size of a request counted), `GccShortFormReplyTests` (the writers and the figure
  check), `GccProjectPageRepositoryTests` and `GccGenerateJobRepositoryTests` (the repository).
- Your run, read from its log: the model and OpenAI's limit for it; two problem searches for each partner with its own "Core
  pain point"; "The tool's remedy" lines in each tool body request; every competitor page that came
  back, and any with no outline named (`left-out`); "Found for:" under each passage; each request's
  size, and nothing left out. Then a Repurpose from an approved page, read on
  the screen.

## What the next run will list

Your next Generate is the first on GPT-4.1, whether or not any of this is built. It is a different
writer: how long its sections run, how it uses the keyword and what it does with a question are all
unknown until that run. Everything measured so far was measured on GPT-4o.

This plan does not remove the keyword lines or the People Also Ask line of the last run; those have
their own causes. The writers are handed about twice what they are today, so I cannot say before a
run whether a section reads differently or a new line appears. A Generate run no longer writes a
Social post, an Email or an Ad; those come from Repurpose afterwards. Nothing here is fixed until
your run says so.
