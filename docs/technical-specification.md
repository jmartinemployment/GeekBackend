# Technical specification: GeekBackend and the Content Creator

**Status:** written 2026-10-07 at HEAD `43fbc15`, from a read of the code of GeekBackend and of the sibling repos
(`content-creator-v2`, `Geek-Crawler`, `Geek-Crawler-v2`, `Geek-Crawler-Rag`, `GeekOAuth`), by read-only explorers.
Names of settings are given and values never are. I spot-checked the solution layout, the framework versions, the
workflow and Dockerfile lists and the hub routes; the rest is the explorers' reading. Anything not established says
**not determined**. Where a document and the code disagree, the code is followed and the document is listed in section 14.

## 1. What the application is

A content-production backend. An operator saves a project brief, presses Generate, and receives pages (pillar, blog,
tool pages, email, social, image prompts) grounded in three sets of crawled sites: the project's own site, its
partners' sites (tools it sells or promotes) and its competitors' sites. Generation is done by GeekAPI. Retrieval and
quote verification are done by a separate RAG service that never generates.

## 2. System map

```
Browser (content-creator-v2, Next.js on Vercel)
   | REST via same-origin BFF /api/cw/*  + SignalR /hubs/workflow-realtime
   v
GeekAPI  (.NET 10, Railway)  -- gateway, generation, guards, export
   |-- X-Repo-Key --> GeekRepository (.NET 10, Railway)  -- the data plane
   |                      |-- Postgres (DATABASE_URL: content_creator, content_creator_v2, content_writer_*, geek_gtm, ...)
   |                      '-- Mongo db geek_crawler (crawl_runs, crawl_pages, crawl_links)
   |-- X-Api-Key --> Geek-Crawler-Rag (Python/FastAPI, Hostinger VPS) -- index, retrieve, verify
   |                      '-- Qdrant (vectors) + the same Mongo (reads crawl data)
   |-- OpenAI / Anthropic / Groq (raw HTTP)
   '-- JWT validated locally against GeekOAuth (.NET 10, OpenIddict, Railway)
Geek-Crawler-v2 (Node/Crawlee, run locally) --ingest--> GeekAPI --> GeekRepository --> Mongo
```

Trust boundaries (from `AGENTS.md`): nothing reaches Postgres except GeekRepository; GeekRepository accepts calls only from
GeekAPI (static key); GeekAPI is the only gateway; browsers exchange the OAuth code server-side and keep only an httpOnly
refresh cookie.

## 3. Runtime and hosting

| Service | Stack | Hosted | Notes |
|---|---|---|---|
| GeekAPI | .NET 10 (`net10.0`), ASP.NET Core | Railway (root `Dockerfile`: sdk:10.0-noble build, aspnet:10.0-noble run, Playwright Chromium installed) | listens on `0.0.0.0:$PORT`, default 8080 |
| GeekRepository | .NET 10 | Railway (`Dockerfile.repository`, self-contained linux-x64, `railway.geekrepository.toml`: restart on failure, max 10) | PORT 5050 |
| GeekOAuth | .NET 10, OpenIddict 7.5.0, EF Core 10.0.8 | Railway (`railway.toml`, healthcheck `/health`) | deploys on push to main |
| content-creator-v2 | Next 16.2.12, React 19.2.4, Tailwind 4, `@microsoft/signalr` 10 | Vercel from `main` | dev port 3003 |
| Geek-Crawler (UI) | Next 16.2.12 | Vercel | operator UI for crawls; not a crawler |
| Geek-Crawler-v2 | Node >=24, TypeScript, Crawlee 3.18.1, Cheerio, Playwright (link harvest only) | normally local | its `serve` API is private (127.0.0.1:8787); no crawler service is deployed (its README) |
| Geek-Crawler-Rag | Python >=3.13, FastAPI 0.141.1, llama-index-core 0.14.24, qdrant-client 1.19.0, fastembed 0.8.1, motor 3.7.1 | Hostinger VPS (Docker, Caddy, GHCR image) | port 8080 bound to 127.0.0.1 |

Where Mongo and Qdrant physically run, and the Postgres host behind `DATABASE_URL`: **not determined** (GeekOAuth's
`.env.example` says Supabase; `AGENTS.md` says its identity database is Railway Postgres).

## 4. The GeekBackend solution

`GEEKBACKEND.slnx` lists five projects, all `net10.0`, nullable and implicit usings on (a sixth,
`scripts/ImportBlogContent`, is outside the solution).

| Project | Type | Key packages | Size (C# lines, files) |
|---|---|---|---|
| GeekApplication | library | HtmlAgilityPack 1.12.4 | 4,520 / 61 |
| GeekAPI | Web | JwtBearer 10.0.7, EF Core 10.0.9, HtmlAgilityPack, Playwright 1.51.0, PdfPig, QuestPDF, RobotsExclusionTools, DotNetEnv | 83,928 / 434 |
| GeekRepository | Web | Npgsql 10.0.3, Npgsql.EFCore.PostgreSQL 10.0.2, Dapper 2.1.72, Polly 8.5.2, MongoDB.Driver 2.24.0 | 21,071 / 178 |
| GeekBackend.Tests | xunit 2.9.3 | EphemeralMongo, EF InMemory | 36,199 / 219 |
| GeekBackend.IntegrationTests | xunit | Mvc.Testing, SignalR.Client | 2,662 / 11 |

GeekAPI references GeekApplication only, has no Npgsql or Mongo reference and reaches data over HTTP. There is no OpenAI or
Anthropic SDK: LLM calls are raw HTTP. SignalR comes from the shared framework. `InternalsVisibleTo` is declared in
`GeekAPI.csproj` for both test projects. GeekAPI has 68 controller classes, GeekRepository 68.

## 5. Authentication and authorization

- **GeekAPI:** one scheme, JwtBearer, authority from `GEEK_OAUTH_AUTHORITY` or `AUTH_SERVER_URL` (startup fails if neither is
  set). `MapInboundClaims=false`, `ValidateAudience=false`, one-minute clock skew, name claim `sub`. SignalR accepts the
  token from the query string. Policy `ContentCreatorManage` requires scope `content-creator.manage`.
  `ApiKeyMiddleware` covers everything else: a JWT principal passes, otherwise `X-API-Key` (`GEEK_BACKEND_API_KEY`) plus an
  optional `X-Geek-User-Id`. Public: `/health`, `/hello`, `/robots.txt`, `/api/case-studies`, `/api/departments`,
  `/api/use-cases`, three OAuth callbacks, `GET /api/blog/*`. `/api/*/internal/*` needs the API key. Bearer validation is
  local (no introspection), as GeekOAuth's own documents intend.
- **GeekRepository:** scheme `RepoApiKey`, header `X-Repo-Key` compared in fixed time with `REPO_API_KEY` (required at startup);
  policy `InternalService` on all controllers; `/health` anonymous.
- **GeekOAuth:** Authorization Code with PKCE required, client credentials, refresh token. Access token 15 minutes, identity
  5 minutes, refresh 14 days. Scopes: openid, profile, email, offline_access, internal.api, devices.manage,
  content-creator.manage. Public PKCE clients include `geek-content-creator-v2`; confidential: `geekapi`,
  `geekatyourspot-website`, `geekgtm`. GeekAPI to GeekRepository does not use OAuth (the static key avoids a circular
  bootstrap).
- **content-creator-v2:** PKCE with S256; cookies `gcc_access` (5 min), `gcc_refresh` (30 days) and `gcc_pkce_verifier`, all
  httpOnly; nothing in browser storage (a test forbids it); a same-origin BFF forwards the bearer token.

## 6. Data

**Postgres (GeekRepository, one connection string, `DATABASE_URL`).** Eight EF contexts; seven are migrated at startup
(only `ContentCreatorDbContext` stops the host on failure); 34 hand-written SQL scripts run through `SqlMigrationRunner`
and `schema_migrations`.

| Context | Schema | Holds |
|---|---|---|
| AppDbContext | default | departments, case_studies (+ metrics, actors, event-flow steps), use_cases |
| ContentWriterDbContext / V2 / V3 / V4 | public / content_writer_v2 / v3 / v4 | legacy writer data (V3: 19 tables) |
| GtmDbContext | geek_gtm | gtm_account_connections |
| **ContentCreatorDbContext** | **content_creator** | gcc_clients, gcc_projects, gcc_project_revisions, gcc_project_log, gcc_creates, gcc_artifacts, gcc_artifact_versions, gcc_version_evidence, gcc_approval_events, gcc_partner_extractions (the extraction bank), gcc_generate_jobs, gcc_generate_job_events (the run log), gcc_site_analyses, gcc_site_findings, gcc_tasks, gcc_time_entries, gcc_deliverables |
| ContentCreatorV2DbContext | content_creator_v2 | 69 `gcc_v2_*` sets (agents, skills, context, task runs, canvas, pipeline). **Dormant:** no live caller reaches them |
| (Dapper, no context) | geek_blog | blog posts |

**Mongo (db `geek_crawler`)**: `crawl_runs`, `crawl_pages`, `crawl_links` (the crawl store, reached through
`MongoGeekCrawlerService` and `repo/geek-crawler/*`); the RAG service also writes `rag_index_jobs`,
`rag_index_scheduler`, `rag_index_intake`, `rag_vector_cache`, `rag_execution_replays`. **Qdrant**: `geek_crawler_chunks`
(dense 384-d cosine plus BM25 sparse) and `geek_ad_templates`. **In memory (GeekAPI):** `GccJobStore`, a singleton
dictionary of running, ready and failed jobs, evicted after six hours; the retrieved evidence for a Generate lives in the
running job and is not stored.

Page corpus format: typed `blocks` (heading, paragraph, listItem, quote, code, row, term, definition) plus `contentHtml`.
Markdown is not used as a corpus, verification or interchange format.

## 7. The Content Creator

**Surface (GeekAPI, `api/geek-content-creator`).** `GccProjectsController` (class policy ManagePolicy): projects (list by
client, get, create, update, status, brief PATCH, soft delete), artifacts, log, **generate** (`POST {id}/generate` returns 202
`{jobId}`; `GET {id}/generate/latest`; `GET {id}/generate/{jobId}/events`), `export/html`, tasks, time, deliverables.
`GccController`: clients, creates, versions (`GET`, `seo`, `polish`, `approve`), keyword sources, image prompts,
SERP parse, partner-quote readiness; `repurpose` returns 403 (disabled). `GccInternalController` (API key): brief backfill.
GeekRepository mirrors these under `repo/content-creator/**` (InternalService).

**Content types.** Live: pillar, blog, tool (one page per partner), email (cold outreach), social (and ads types), image
prompt. Disabled by `DisabledContentTypes` (13 keys): tech article, comparison, alternatives, case study, guide, listicle,
service, local, whitepaper, LinkedIn document, three email variants. Only pillar, blog and tool have registered prompt sets.

**Generate, end to end.**
1. `GccProjectsController.Generate` validates the actor, provider, types, declared URL index, site crawl and brief; inserts the
   job row (`409` if one is running); returns 202.
2. `GccGenerateJobRunner` starts `Task.Run` (fire and forget, own DI scope, no cancellation) and opens the run log.
3. `GccGenerationCoordinator.RunGenerateAsync`: validate types; **delete the project's old pages of the requested types**
   (a run asked for some partners' tool pages deletes those tool pages and no others: `GccPartnerToolSlices.Chosen` answers
   for the delete and for the writer, and the repository's delete takes the page names; until 2026-10-12 it deleted every
   tool page, and the 11:37 UTC run that day, asked for one, deleted five and was refused);
   resolve grounding once (`GccGroundingResolver`: one search per crawl type, 32 passages per partner site, 8 for the project
   site and competitors); fan out over the types with `Task.WhenAll`, each through `GccGenerateService`; settle
   (`GccRunSettlement`); persist the pieces that wrote in one write; the job fails only when no type wrote anything.
4. `GccGenerateService` (3,798 lines) writes: lede, batched body (two sections per call), FAQ, closing (`GccClosing`, built by
   code), image prompts, metadata and JSON-LD; every draft is checked once by `GccDraftGuard` and either refused or shipped
   with gaps reported. No retries.
   - **Every body call carries the page's whole evidence**, so the evidence decides a call's size. A passage the Library
     returns as a child is printed as its parent (`Context:`) and itself (`Specific detail:`); when the two are the same
     text it is printed once, with neither label (`HttpGeekCrawlerRagClient.RenderChunk`).
   - **A call reserves room for its reply, and the reservation counts against the model's limit whether or not it is used.**
     A call that writes a part of the page, two sections at most, reserves 8,192 tokens; a call that writes a whole outline,
     or more than two sections, reserves 16,384 (`ContentPromptBuilder.BodyMaxOutputTokens`).
   - **What a model accepts in one request is in code, and a request is counted against it before it is sent**
     (2026-10-12). `OpenAiModelLimits` holds it for each model family: `gpt-4.1` 1,047,576 tokens, `gpt-4o` 128,000,
     what is sent and the reply's reservation together, matched from the start of the model's name. A model that is not
     listed is refused, naming it: at the start of a run before its old pages are deleted
     (`GccGenerateService.ModelRefusal`), and again where any request to OpenAI is sent (`OpenAiProvider`). Until then no
     code knew the limit: on 2026-10-10 a pillar body call of 125,253 prompt tokens was sent, refused with 400 and its
     page not written. Naming a new model in `LlmProviders__OpenAi__Model` now needs its line in that table first.
   - **The count** (`GccRequestSize`) is OpenAI's own encoding (o200k_base). Checked against the 40 requests of the
     08:17 UTC run of 2026-10-07: the 16 with no reply format came to OpenAI's figure exactly; the 24 with one came to
     it less a fixed 383 or 368 for the format, which OpenAI counts its own way. The format is counted as its own text
     (389 tokens for "sections" today), so that part is near and not exact. A request of 370,000 characters takes about
     14 ms to count. Every request's count is on its `call` event (`countedTokens`, with `accepts`) beside OpenAI's
     `promptTokens`, so the counter is checked by every run.
   - **Every question section is answered from searched passages.** A tool page's FAQ questions are each searched for in
     that partner's crawl; the pillar's People Also Ask and the blog's FAQ questions are each searched for in every
     partner's crawl (`GccGroundingResolver.PageFaqSearches`), three passages a search. Only a question with passages is
     sent to the model, the writer is told to answer from them alone, and a question left out is reported with why. No
     code checks an answer's sentences against its passages. A pillar or blog question call carries at most 200,000
     characters of passages (`GccPageFaqEvidence.MaxEvidenceCharsPerCall`), the one call whose size code bounds.
5. The runner records `completed` or `failure`, writes the job row, and pushes over SignalR.

**The brief, as a writer is given it.** Each pillar, blog and tool page call prints the brief once. A choice the form stores
as a code (`in_market`, `commercial_investigation`) is put into words by `BriefChoiceWords`, the one place every prompt reads.
The pillar's opening carries the operator's whole framing; a blog's and a tool page's opening carries the problem alone
(`GccNicheFraming.HookGuidance`). The brief's gate (`GccGenerateService.ValidateBriefRequired`, read by both Generate routes)
refuses a missing required field and a taxonomy path whose first level is not one of `Departments.Slugs`: that level is the
department directory of every address the run builds. `BriefFieldReachTests` records which field reaches which call.

**A partner's own rows, on the pillar and the blog.** The brief's rows of failure and remedy are each entered for all
partners or for one. A row for all partners is the category's: its failure is what the pillar's and the blog's sections
cover. A row for one partner is searched for on that partner's site, and since 2026-10-11 every body call of the pillar and
the blog is told it under that partner's name, beside the list of partners the page must name (`GccPartnerAnswers`): the
failure, and what the operator noted the partner does about it. It is the operator's note, not evidence: the writer is told
to say what a partner does only as that partner's passages show it and to take no figure from the note. No code checks a
sentence about a partner against a passage. The partner's own tool page is told its failures and not its remedies, as
before. On the accounts receivable project the block is about 6,400 characters, 1,260 tokens a body call, worked out from
the brief's field lengths and not yet seen in a real run.

**A partner's own problem and pitch, on the pillar and the blog.** Besides its rows a partner has two boxes of its own in
the brief, `perTool[host].coreProblem` and `perTool[host].automationToPitch`. Until 2026-10-12 they reached that partner's
tool page and no other page. Since then the same block prints each under the partner's name, the problem before its rows
and what the operator would pitch it on after them, in every pillar and blog body call (`GccNicheFramingReader.OwnStatementsForHost`).
Only what the operator entered for that partner is printed: the category's is not put in an empty box's place, because the
page already carries the category's as its own framing. A partner with a problem or a pitch and no row is listed with what
it has. A pitch is a selling line and may name a capability or a figure; the writer is told to make its case in its own
words, never verbatim, to take no figure from it, and to state a capability only as that partner's passages show it. A
partner's `painPoints` still reach its own tool page alone. It adds about 2,800 characters, 555 tokens a body call, on the
accounts receivable project (about 117,050 becomes about 117,600 of 128,000) and about 475 on the spreadsheet data project,
worked out from the briefs' field lengths. Not yet seen in a real run.

**A brief with no row for all partners.** The operator's research is per partner, so a brief can hold every failure under
a partner and none for the category. The pillar's two sections on what is going wrong and the blog's section on what the
work costs by hand were then told no failure: on the 13:14 UTC run of 2026-10-11, the first brief entered that way, 25
failures were in the brief and neither page was told one. Since 2026-10-11 those sections are sent to the partners'
failures in the block above (`GccNicheFramingReader.ForEveryPartner`, `GccNicheFraming.FailuresGuidance`), and the block
says the operator named no others. They are told to argue from them and add none of their own, not to cover every one:
five partners with five failures each do not fit one section. One answer decides both wordings
(`GccPartnerAnswers.CarryThePagesFailures`), so a section is never sent to a block that lists no failure. A brief with a
failure for all partners is read as before. Not yet seen in a real run.

**An outline's section label names no one subject's features.** The tool page's section on what gets configured read
"approval chains, routing, automation logic", and the blog's section on early decisions "approval routing", written when
every project was accounts payable. On the same run three cash flow forecasting tools were each given a section headed
"Approval Chains, Routing, and Automation Logic". The tool label now reads "its own rules and automation logic", named as
the product's own pages name them, and the blog's names data, mapping and who owns what. The Workflow product's
per-section writer still lists "approval chains, routing" (`BuildImplementationSectionGuidance`) and is not changed.

**Each passage keeps what found it** (2026-10-11). A partner's crawl is
searched with the brief's problem and then one search per row. Which search found which passage was dropped where the
results came back, and the kind of page the index marks each page as (pricing, docs, blog, product, about, careers,
legal, compare, page) was sent on every result and not read. Both are now kept on the page (`GccQuoteablePage.Category`,
`Searches`, `FoundBy`) and listed on the run log's `grounding` event (`poolSearches`, `pageKinds`). No writer is shown
either. They are two of the three things a request's passages are chosen by, below.

**How a request's passages are chosen when not all of them fit.** Each pillar and blog section names one of seven
labels for the part of the research it is written from (`SectionSlot.ResearchLabel`; `GccResearchLabel`). Code gives each
passage its labels from the kind of page, the searches that found it and its own words (`GccPassageClassifier`). The run
log gets `evidence-labels` once a page and `evidence` once a request (`GccResearchPerRequest`).

**The one limit is what the request's model accepts** (2026-10-12; Jeff: "STOP NOT USING DATA, EVEN IF THAT MEANS IT
COSTS MORE"). Every opening and body request of a pillar, a blog and a tool page is built with all of its page's partner
research, counted, and sent as it is when it is within what its model accepts with the reply's reservation
(`GccGenerateService.RequestThatFitsAsync`). Nothing is chosen while everything fits. Until that day the code held
three numbers of its own (research whole up to 350,000 characters, otherwise 40,000 for each partner, 240,000 in all),
sized for a model of 128,000 tokens, and they cut research that fitted; a tool page was never counted at all.
- **Over the limit**, the room for partner research is what the model accepts, less the reply's reservation, less the
  request built with none of the partner research, turned into characters at the rate that page's own research counts
  at. The room is shared between the partners equally, and what a partner with less research does not use goes to the
  others (`GccResearchSelector.SharesOf`). A partner is carried up to its part: passages labelled for the request's
  sections first, then general ones, then its others, a passage an earlier request of the page carried counted at half.
  A tool page's sections name no label, so its passages are taken in the search's own order.
- **A request that is over with none of the partner research in it is refused before it is sent**, saying its size.
- **The record** (`evidence`) carries the request's size before it is sent: `request.model`, `accepts`, `reply`,
  `tokens`, `withReply` (what is held to the limit) and `withEverything`; `room` and each partner's `share` when the
  research was chosen from; and each passage chosen and left out.
- **Why a partner is filled to its part, and what choosing costs.** From the 14:20 UTC run of 2026-10-11 (250,717
  characters in 213 passages): none was without a label and only 8 were for "getting it running", so a request for
  those sections would have carried a fifth of the research if labels decided what was carried and not the order. And
  its pillar's body requests were billed at the lower price for 66,432 of about 68,300 tokens from the second on, which
  a request with its own choice of research loses. In the classifier a word and its other forms count once, and "plan",
  "timeline", "results", "review", "faster" and "increased" are out of the lists.
- **Still a page's first six passages and first eight headings.** `GccResearchSelector.PassagesOf` and
  `BuildResearchBlock` both stop there (`GccResearchCaps`); that count, and the other counts that cut research, are the
  next step of `plans/use-everything.md`.
- **Only the partner research differs between requests.** The publisher's positions, the competitor blocks, the site's
  own coverage, the partners to name, what each partner answers, the heading sources and the note on amounts not in
  dollars are whole in every request.
- **The page's checks read all of the research.** Figures, money, quotations and headings are judged against what the
  search returned, not against what a request carried, so a page is not refused because a request was shown less.
- **A request sent a part is told so in one line** (`GccGenerateService.ResearchIsAPartNote`): the passages are the ones
  chosen for its sections, and a passage that is not there is not evidence that a product lacks something.
- **A partner is the project's partner whose site the page is on or under.** A partner's help or documentation
  subdomain was a partner of its own with a limit of its own (`GccResearchSelector.PartnerOf`).
- **The record of a request** lists what the research holds, what the request carried, the same partner by partner,
  and each passage chosen and left out. A test fails if a request is sent anything other than what its record lists.
- **Not seen on real research.** No run has yet chosen: every project run so far is far under what GPT-4.1 accepts
  (the largest request of the 15:29 UTC run of 2026-10-12 was about 75,000 tokens of 1,047,576), so every `evidence`
  event of the next run should say `whole`. The choice is tested through the real writers against the smaller model.
- **Do not add a number of the code's own that leaves research out**, and do not put a word back in the classifier's
  lists without a run that shows it labels the right passages.
- **The research holds almost nothing for some sections.** On the 14:20 UTC run, 8 of 213 passages were labelled for
  getting it running and 7 for results and proof; of the ones read, about half of the first and none of the second were
  really that. Those sections are written with next to no evidence of their own, chosen or whole. Choosing cannot find
  what was never searched for, and nothing searches for those sections.
- **The plan this was built from is removed** (2026-10-12, the work being finished). It is in git:
  `git show d4f1f3b:plans/research-classification.md`.
- **The tool pages are not changed.** Each carries one partner's research and fits.

**The 13:14 UTC run of 2026-10-11** (project "Cash Flow Forecasting: Automated Spreadsheet Data", five partners, 30 body
searches and 123 question searches) is the first run since 2026-10-10 to write a pillar and a blog. All seven pages were
written and none refused. A pillar body call was about 66,500 prompt tokens with 8,192 reserved, about 74,700 of 128,000;
the accounts receivable project's calls, about 107,600, have still not been run. The pillar answered five of its eight
questions and the blog four of its six from searched passages, and the five left out were listed; the tool pages answered
52 of 53. The pillar was 4,076 words and the tool pages 3,858 to 4,468. Fifteen lines were listed, eight of them the
keyword: the writer wrote "Automated Spreadsheet Data" two to four times on a tool page and code found no shortened form
to restore on any of the five.

**Checks (`GccDraftGuard`).** Refuse: names-product, quotation and one-quotation (tool), no-quotation (pillar, blog),
tools-section, heading-provenance, unlisted-tools, links, link-text (a link may sit on 12 words at most), numbers, currency,
questions-quiz. Report only: partner-mentions, closing-link, opening-links, blockquote-missing (tool), keyword-density
(the page's keyword share outside the score's band), keyword-heading (no heading on the page carries the phrase),
keyword-heading-by-hand (a heading puts the phrase straight after "manual") and page-length (the page under its type's
word floor, with the calls under their own floor listed after it; a page at its floor lists no call). Nothing is reported
for a single call on a page that passes.

**numbers reads a scaled figure in the evidence as the number it stands for** (2026-10-12, `GccFigureGrammar.NumbersIn`).
A figure in a draft must be a number in its evidence, compared by value. "200K+", "$300M", "$10m", "2bn" and "1.5 million"
in the evidence license 200,000, 300,000,000, 10,000,000, 2,000,000,000 and 1,500,000 as well as their digits as written;
a small m or b is a scale only on an amount of money. Until then "200K+" licensed 200 alone: the 15:29 UTC run of
2026-10-12 (project "Tax Compliance & Regulations: Complex and Changing Laws") refused its blog for "200,000 hours saved",
written out from Zamp's own "200K+ hours saved". What counts as a figure in a draft is unchanged, so a draft's own "200K+"
is still not checked against anything. Not yet seen in a real run.

**Realtime.** SignalR hub `/hubs/workflow-realtime` (also `/hubs/gcc-v2-realtime` and `/hubs/geek-crawler-realtime`); a
client calls `JoinGccGenerate(jobId)` and receives `GccGenerateEvent`, `GccGenerateTypeEvent`, `GccGeneratePreflightEvent`.
Push failures are logged as errors and recorded in the run log; they never fail a job.

**Run log.** Table `content_creator.gcc_generate_job_events` (id, job_id, seq, at, kind up to 32, piece up to 128,
payload_json). Kinds written: started, left-out (pages a search returned that are not evidence: a legal document, or a
template page still carrying a page builder's placeholder text, each with why), grounding (with each FAQ search: its
passage count, how the library ordered it and what it scored each passage), call (prompt, response, model, tokens, finish reason), batch (a call's words, floor, headings
and keyword uses), keyword (the page's count, before and after, each edit and each section's share, uses and places), faq
(what became of each FAQ question, with its passages' scores), verdict, warning, fault (exception type, stack, inner
exceptions), outcome, settled, failure, completed.

**Export.** `GccArtifactExportService` renders a full HTML document per page through `SectionHtmlRenderer`, the only place
tags are produced: `<h1>` title, hero summary, lede, sections, JSON-LD (`Article` for pillar, in a `@graph` with the FAQ
when there is one; `SoftwareApplication` for tool pages). Bodies are a `ContentDocument` (sections of text, list, quote, code,
definition paragraphs); the model returns content and never markup.

## 8. Language models

Providers `OpenAi`, `Anthropic`, `Groq`, each behind a concurrency gate (`LlmProviders:MaxConcurrentCalls`, default 4);
Generate accepts OpenAi and Anthropic. Config: `LlmProviders:{provider}:{BaseUrl, ApiKey, Model, TimeoutSeconds}` (default
timeout 120 s), OpenAi also `ExtractionModel` and `UtilityModel`, plus `DefaultProvider` and an `Enabled` kill switch. Model
names are not defaulted in code (an empty one is refused, and so is an OpenAI model `OpenAiModelLimits` does not list);
the 2026-10-07 run log shows `gpt-4o-2024-08-06` (38 calls) and `gpt-4o-mini-2024-07-18` (2), and the writing model has
been `gpt-4.1` since Jeff changed `LlmProviders__OpenAi__Model` on 2026-10-12. OpenAI's reply states the account's
allowance (`x-ratelimit-limit-tokens`, `-remaining-tokens`, `-reset-tokens`); it is on each request's `call` event as
`rateLimit` and nothing acts on it. OpenAI and Groq use `response_format: json_schema`; Anthropic uses forced tool use. Body
batches carry a provider-enforced schema; ledes do not. There is no retry or Polly in GeekAPI. OpenAI is the live provider.
Per run, OpenAI's automatic prompt cache served 54% of input tokens (2026-10-07 log).

## 9. The RAG library

FastAPI. `POST /v1/index {runId}` (409 without `ContentReadyAt`), `POST /v1/query` (hybrid dense plus BM25 with reciprocal
rank fusion; optional Cohere rerank, which has no key, so no search is reranked: five local reranking models were tried on
2026-10-10 against that day's run and none ranked the answering passage first, so none was built in and no score cut-off
was either; the trial is in git, `git show dce00d2:plans/fix-the-2026-10-10-generate-run.md`), `POST /v1/verify` (whitespace-normalized quote-in-text against the block projection),
`/v1/pages`, `/v1/templates`, `/v1/capabilities`, `/health`. Embeddings are local (BAAI/bge-small-en-v1.5, 384-d; BM25 sparse).
Chunking is parent/child cut at headings (child 200/40 tokens, parent 500/80). The index queue is in-process with Mongo leases
at concurrency 1; there is no automatic recovery after a restart. Auth: `X-Api-Key` equals `API_KEY`. GeekAPI reaches it
through `IGeekCrawlerRagClient` (`GEEK_CRAWLER_RAG_URL`, `GEEK_CRAWLER_RAG_API_KEY`, 6 min timeout); it pushes index status
back through a webhook.

## 10. Crawling

Two crawl paths exist. **Geek-Crawler-v2** (Node, Crawlee, Cheerio, static HTTP with a mobile user agent, robots.txt
respected, no retries, 60 s handler timeout, 2,500 pages per site, concurrency 1 to 32) extracts typed blocks and ingests
through GeekAPI (`POST /api/geek-crawler/ingest/runs`, pages batched at 100, links at 10,000) with `X-API-Key`. **GeekAPI**
also contains an in-process crawler (`GeekCrawlerService`, same-origin BFS, Playwright, workers set by
`GEEK_CRAWLER_WORKER_COUNT`) whose data goes to Mongo through GeekRepository. Which one the operator uses today: **not
determined**. Playwright is legacy, awaiting a decision (see `AGENTS.md` notes).

## 11. Background services in GeekAPI

`GccInterruptedJobsOnStartup` (fails running jobs left by a redeploy; one instance is assumed), `GccV2JobWorker`,
`GccV2ContextIngestionWorker` (polls GeekRepository every 45 seconds; nothing enqueues work for it),
`GeekCrawlerPlaywrightStartupHostedService`, `GeekCrawlerWorker` (x count), `GeekCrawlerConfigLogger`,
`GeekCrawlerStallRecoveryHostedService`, `GeekCrawlerScheduleHostedService`. `BackgroundServiceExceptionBehavior` is Ignore.

## 12. Configuration

**GeekAPI `appsettings.json`:** `CompanyProfile` (PublisherName, PublisherLogoUrl, AuthorName, ArticleBaseUrl, BlogBaseUrl,
ToolBaseUrl, ConsultationAnchorHref, ConsultationCtaLabel, ConsultationClosingLead, ConsultationClosingLink,
ConsultationClosingLinkWithoutQuestions, DefaultBlogAuthorId, GtmContainerId, ImplementerPositioning); other sections read in
code: `LlmProviders`, `ContentCreatorV2` (DraftingEnabled, ContextPolicy, ContextObjectStore, MalwareScanner), `GccV2Skills`.
**Environment, GeekAPI:** PORT, ASPNETCORE_ENVIRONMENT, CORS_ORIGINS, REPO_URL, REPO_API_KEY, GEEK_BACKEND_API_KEY,
GEEK_OAUTH_AUTHORITY, AUTH_SERVER_URL, AUTH_URL, OAUTH_CLIENT_ID, CLIENT_SECRET_GEEKAPI, GEEK_CRAWLER_RAG_URL,
GEEK_CRAWLER_RAG_API_KEY, GEEK_CRAWLER_WORKER_COUNT (and other GEEK_CRAWLER_*), OPENAI_API_KEY, ANTHROPIC_API_KEY,
GROQ_API_KEY, GEEK_CONTENT_MODEL_<STAGE>, IMAGE_GENERATOR_BASE_URL, GEEKATYOURSPOT_GITHUB_TOKEN, WORDPRESS_URL, GEEK_CC_GSC_*,
GEEK_CC_DRIVE_*, GEEK_CC_SHAREPOINT_*, GEEK_CC_OCR_*. **GeekRepository:** DATABASE_URL, PORT, REPO_API_KEY, MONGO_CRAWLER_URL,
SKIP_AUTH_SQL_MIGRATIONS, GEEK_SEO_ENCRYPTION_KEY. **content-creator-v2:** NEXT_PUBLIC_GEEK_API_URL, NEXT_PUBLIC_AUTH_URL,
NEXT_PUBLIC_OAUTH_CLIENT_ID, NEXT_PUBLIC_APP_URL, NEXT_PUBLIC_WORKFLOW_HUB_URL. **RAG:** MONGO_CRAWLER_URL, MONGO_DB_NAME,
QDRANT_URL, QDRANT_COLLECTION, QDRANT_API_KEY, EMBEDDING_MODEL, API_KEY, INDEX_STATUS_WEBHOOK_URL, INDEX_STATUS_WEBHOOK_KEY and
more (`config.py`). **GeekOAuth:** DATABASE_URL, ISSUER_URL, SIGNING_CERT_*, ENCRYPTION_CERT_*, CLIENT_SECRET_*, CORS_ORIGINS,
SMTP_*.

## 13. Build, test and delivery

- **Tests:** `dotnet test GEEKBACKEND.slnx`. GeekBackend.Tests: xUnit, 1,972 tests at this HEAD (theory rows included), using
  EphemeralMongo and EF InMemory with scripted model providers, so they prove wiring and the checks and nothing about model
  behaviour. IntegrationTests (33 tests) need `TEST_DATABASE_URL` and run the app through `WebApplicationFactory`.
  content-creator-v2: `node --test`, 14 tests plus a CI of typecheck, lint and build.
- **CI:** the only GeekBackend workflow is `geek-crawler-playwright.yml` (daily and manual). There is no build or deploy CI;
  Railway deploys on push to `main`, and a push restarts the API, which ends a running Generate.
- **Not in the repo:** a Railway healthcheck path and start commands for GeekAPI (**not determined**).

## 14. Where documents disagree with the code

`README.md` names `GeekBackend.sln` (it is `GEEKBACKEND.slnx`). `content-creator-v2`'s `README.md` and `architecture.md` name
the hub `/hubs/gcc-v2-realtime` and the key `NEXT_PUBLIC_GCC_V2_HUB_URL`; the code reads `/hubs/workflow-realtime` and
`NEXT_PUBLIC_WORKFLOW_HUB_URL`, and `architecture.md` names the API prefix `api/geek-content-creator-v2` (the code uses
`api/geek-content-creator`). The Geek-Crawler-v2 README says "no browser" while its link harvest uses headless Chromium and
says seven block kinds while the code has eight (`row`). `GeekOAuth`'s `.env.example` names Supabase for a database `AGENTS.md` places on Railway. Integration tests
`GccV2AgentApiContractTests`, `GccV2TaskAgentApiContractTests` and `GccV2SkillApiContractTests` call routes that no
controller has served since 2026-10-03.

## 15. Known limits and open issues

See `plans/real-run-findings-2026-10-07.md` (writer output under its word floors, the lede with no enforced schema, the
JSON-LD name and `@graph` form, the FAQ without evidence) and `plans/single-source-of-responsibility.md` (the plan to divide
the application by single responsibility). The `content_creator_v2` schema and its workers are dormant.

**A rate refusal from OpenAI (a 429) is not handled, on a decision** (Jeff, 2026-10-12: "File suggestion, in case the
problem occurs, but take it out"). A 429 is OpenAI's reply when the account has sent more tokens or requests in a minute than
its allowance; the request is not processed and not billed. Today such a request fails its page like any failed request:
`OpenAiProvider` logs OpenAI's status and reply and throws, and the run log's `outcome` for that page carries the message.
None has occurred (GeekAPI's Railway log back to 2026-10-05 holds none). GPT-4.1's allowance is 450,000, 2,000,000 or
30,000,000 tokens a minute by account level, the same as GPT-4o's, with a separate one for requests over 128,000 tokens
(500,000, 5,000,000 or 10,000,000), read from OpenAI's model pages on 2026-10-12; the 15:29 UTC run of that day sent about
700,000 in one minute and was not refused. Up to four requests are in flight at once (`LlmProviders:MaxConcurrentCalls`).
**The suggestion on file, from an outside review of 2026-10-12, in case one does occur:** (1) hold a request at
`LlmConcurrencyGate` while the tokens sent in the last minute plus its own would pass the limit, with the limit read from
OpenAI's `x-ratelimit-limit-tokens`, `x-ratelimit-remaining-tokens` and `x-ratelimit-reset-tokens` reply headers and the
request's size counted before it is sent (that count is not built; `plans/use-everything.md`); or (2) on a 429 only, wait the
time OpenAI's reply names and send the same request again, doubling the wait on each further refusal. The second is a retry,
and the standing rule is no retries (Jeff, 2026-10-06), so it needs his word.
