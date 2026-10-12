# Use everything: the Brief, the research, and Repurpose for the short types

**Status:** written 2026-10-12. Last updated the same day, for GPT-4.1 and for the outside review
you pasted. Not approved. Nothing is built.

It replaces the earlier plan of the same day (one research block per content type), deleted on
your word: this plan drops its 40,000 for each partner and does not include its rewrite.

## Changed since you last read this plan

- **The model.** GeekAPI restarted at 22:04 UTC on 2026-10-12 with the variable you changed, and is
  up. GPT-4.1 accepts 1,047,576 tokens in a request; GPT-4o accepted 128,000. I have not read the
  variable's value, because listing the variables would show the keys. The first request of your
  next run shows the model on its run log.
- **Nothing is left out for size.** The largest request in this plan is about 161,000 tokens, 15%
  of what GPT-4.1 accepts.
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
| Counting tokens for 65 large requests will lag | A Generate is a background job, and each request is counted on the task that builds it, beside an OpenAI call that takes seconds. GeekAPI runs on Railway, not Hostinger | No new machinery. The counter's test times a 370,000-character request and the commit records it. If it is over a tenth of a second, a page's research, the same text in each of its body requests, is counted once |
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
quotes none of them and takes no figure from them. A page's checks do not read them.

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

Worked out from the run log, not measured. Not in the sum, because the log does not size them: an
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
   - It does not fit: competitor pages beyond the first five give way first, each with its outline.
     Then each partner gets an equal share of the room, the passages for the request's sections
     first. Every passage or page left out is listed on the run log.
   - Tool pages go through it as the pillar and blog do.
   - Each request's size is on the run log before it is sent.

The reply's reservation stays as it is: 8,192 tokens for a request that writes two sections, 16,384
for one that writes more. GPT-4.1 allows a reply of 32,768.

**Cost.** A run of a pillar, a blog and five tool pages sends nearly twice the tokens it does
today. GPT-4.1 charges $2.00 a million for new text and $0.50 for repeated text; GPT-4o charged
$2.50 and $1.25. Worked out, a run after this plan costs about what that run cost on GPT-4o,
roughly $4. That assumes a tool page's later requests are billed as repeats the way the pillar's
were; I read that from the log for the pillar and the blog only.

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
  deleted with the old writer (`GenerateToolAsync`, `GenerateRepurposePackAsync`). On 2026-10-10 you
  said to keep Repurpose: this keeps it and removes those two branches. Say if either should stay.
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

**The limit**
- `Workflow/Providers/ProviderOptions.cs`: what each model accepts, beside `OpenAiOptions.Model`:
  `gpt-4.1` 1,047,576 and `gpt-4o` 128,000, matched from the start of the name. A model in neither
  refuses the run before any paid request and names the model.
- New `GccRequestSize.cs`: the exact token count of a request (system, user and reply format), with
  `Microsoft.ML.Tokenizers` added to `GeekAPI.csproj`.
- `GccResearchSelector.cs`: `Select` (`:221`) takes the room; `WholeUpToChars`, `MaxCharsPerPartner`
  and `MaxCharsInAll` (`:100-109`) are deleted; `PassagesOf` (`:154`) counts every passage of a page.
- `GccGenerateService.cs` `ChosenEvidenceAsync` (`:3425`) and `GccResearchPerRequest`: build the
  request with everything, count it, choose only when it is over. The tool page's opening and body
  call the same thing.
- Rate: `Workflow/Providers/OpenAiProvider.cs` reads OpenAI's `x-ratelimit-limit-tokens`,
  `x-ratelimit-remaining-tokens` and `x-ratelimit-reset-tokens` from each reply, and `GccRunLog`
  puts them on the `call` event. Nothing else changes: no holding and no second send.
- Left as they are: the reply reservations (`ContentPromptBuilder.cs:270-299`) and the size of a
  question request (`GccPageFaqEvidence.cs:46`, which splits questions across requests and leaves
  nothing out). Their comments, and `GccResearchSelector.cs:87-88`, give 128,000 as the model's
  limit and are corrected.

**Part 1**
- `GccNicheFraming.cs`: `Merge` (`:605`) keeps the category's statement and the tool's own, for the
  problem and for the pitch; `Render` and `HookGuidance` print both; one unmerged reader for a
  tool's own section in place of the three copies of that loop.
- `GccGroundingResolver.PartnerQuestions` (`:953`): one problem search for each statement.
- `GccPartnerAnswers.cs`: a block for one tool's page (both problems, every failure with its remedy,
  both pitches); `Instruction` gains the rows saved for all partners with their remedy, and a
  partner's failures saved without a row.
- `GccGenerateService.cs` `GenerateToolPageAsync` (`:1375`, `:1642`): the block joins every body
  request. Not `toolGuardInputs` (`:1620`).
- `GeekAPI/Controllers/ContentCreator/GccController.cs:978`: the Brief screen's check.
- content-creator-v2 `NicheFramingPanel.tsx`: the hint that says the category's is used when a
  tool's is empty.

**Part 2**
- `GccGenerateService.cs`: the `Take(...)` at `:421-446` (research block), `:2858-2863` (your site)
  and `:2911-2920` (competitor research) go.
- `BuildCompetitorHeadingBlock` (`:2937`): the cut at 25 headings (`:2968`) goes. Its pages (`:2957`)
  become the competitor pages the search returned (`CompetitorQuoteables`), matched by address to
  the outlines `GccCompetitorAnalysisResolver.ResolveAsync` reads from every page of every
  competitor's crawl. `BuildProvenanceEvidence` (`:3806`) still reads every outline, so a
  `competitor:` tag is checked as it is today. The match is one comparison of two addresses
  (`http`/`https`, `www.`, a trailing slash, `#...` and case ignored), written once and used
  nowhere else for now. Which address the outline carries, the page's first or the one after a
  redirect, is read before the match is written. A returned page with no outline is a `left-out`
  line on the run log.
- `BuildResearchBlock` (`:339`) prints "Found for:" from `GccQuoteablePage.SearchesThatFound`; the
  text the checks read is built without it, so a figure in your note licenses nothing.
- Pillar and blog: the passages `GccPageFaqEvidence.FoundFor` returns for the page's questions join
  the body's research, each passage once. The checks already read them (`PageFaqEvidenceText`).
- Tool page: `GccPartnerToolSlices.Bucket` (`:156`) uses `GccResearchSelector.PartnerOf`; the body's
  evidence (`:1473`) gains the competitor heading block, worded without the heading-tag rule; the
  hook gains `BuildPublisherPositionsBlock`; the partner's question passages
  (`research.FaqEvidence`) join the body's research, each passage once.
- `Partner/GccPartnerExtractionService.cs:47-48`: no cut at 1,200, no first 40.
- `GccTypedPassageReader.cs:39`: read in groups of 32 until every page is read.

**Part 3**
- `GccController.cs:694-808`: the route validates and starts a job through `GccGenerateJobRunner`;
  `RepurposeInternal`, `MixRequest`, `FormatPackBody` and `BuildPackChannels` are deleted.
- `GccGenerationCoordinator.cs`: the email and social arms of `GenerateOneAsync` (`:611`, `:621`)
  take the source page; `ValidateRequestedTypes` refuses the three without one; `DeleteOldPagesAsync`
  deletes that page's pieces of the asked types and nothing else; `PersistAllAsync` saves under the
  page; the comment at `:266-279` is corrected.
- `GccGenerateService.cs`: `GenerateEmailAsync` and `GenerateSocialPostAsync` (`:2368-2478`) take
  the page's text, read with `GccBodyEnvelope.Read` and `ContentDocumentText`; the pack writer and
  its helpers (`:1124-1189`) and `GenerateToolAsync` (`:1824`) are deleted. The figure check is
  `Guardrail/GccFigureGrammar`.
- GeekRepository: `GccGeneratedPiece` gains the page a piece derives from;
  `GccProjectPageRepository.SaveGeneratedAsync` and the delete take it; a migration adds one piece
  for a page, type and name, as an index created by SQL like
  `ux_gcc_artifacts_one_page_per_type_and_name`.
- content-creator-v2: `src/lib/content-types.ts` (the three leave Generate's list; their tabs
  stay), `ProjectContentWorkspace.tsx` ("Approve & export", and the line under Generate's list that
  says how many pieces are deleted with a ticked page), `src/services/gcc-api.ts` (the call, and
  `renderArtifactBody`), `src/lib/types.ts` (`RepurposeMixRequest` deleted).

## Verification

- After every commit: `dotnet build GeekAPI`, `dotnet test GeekBackend.Tests` (2,003 today),
  `dotnet test GeekBackend.IntegrationTests` (44); the form's tests (52) for its commits.
- The counter is checked against requests of the 15:29 run whose sizes OpenAI reported. GPT-4o and
  GPT-4.1 count tokens the same way.
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
  It also fails if the outline of a competitor page no search returned is printed.
- Repurpose: tests through the real route for an approved page, a page not approved, a project with
  no crawled site, a figure the page does not hold, and a second Repurpose replacing the first; the
  repository's tests for saving and deleting a page's pieces.
- Your run, read from its log: the model and OpenAI's limit for it; two problem searches for each partner with its own "Core
  pain point"; "The tool's remedy" lines in each tool body request; every competitor page that came
  back; each request's size, and nothing left out. Then a Repurpose from an approved page, read on
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
