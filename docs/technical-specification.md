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
3. `GccGenerationCoordinator.RunGenerateAsync`: validate types; **delete the project's old pages of the requested types**;
   resolve grounding once (`GccGroundingResolver`: one search per crawl type, 32 passages per partner site, 8 for the project
   site and competitors); fan out over the types with `Task.WhenAll`, each through `GccGenerateService`; settle
   (`GccRunSettlement`); persist the pieces that wrote in one write; the job fails only when no type wrote anything.
4. `GccGenerateService` (3,798 lines) writes: lede, batched body (two sections per call), FAQ, closing (`GccClosing`, built by
   code), image prompts, metadata and JSON-LD; every draft is checked once by `GccDraftGuard` and either refused or shipped
   with gaps reported. No retries.
5. The runner records `completed` or `failure`, writes the job row, and pushes over SignalR.

**Checks (`GccDraftGuard`).** Refuse: names-product, quotation and one-quotation (tool), no-quotation (pillar, blog),
tools-section, heading-provenance, unlisted-tools, links, link-text (a link may sit on 12 words at most), numbers, currency,
questions-quiz. Report only: partner-mentions, closing-link, opening-links, blockquote-missing (tool), keyword-density
(the page's keyword share outside the score's band), keyword-heading (no heading on the page carries the phrase),
keyword-heading-by-hand (a heading puts the phrase straight after "manual") and page-length (the page under its type's
word floor, with the calls under their own floor listed after it; a page at its floor lists no call). Nothing is reported
for a single call on a page that passes.

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
names are not defaulted in code (an empty one is refused); the 2026-10-07 run log shows `gpt-4o-2024-08-06` (38 calls) and
`gpt-4o-mini-2024-07-18` (2). OpenAI and Groq use `response_format: json_schema`; Anthropic uses forced tool use. Body
batches carry a provider-enforced schema; ledes do not. There is no retry or Polly in GeekAPI. OpenAI is the live provider.
Per run, OpenAI's automatic prompt cache served 54% of input tokens (2026-10-07 log).

## 9. The RAG library

FastAPI. `POST /v1/index {runId}` (409 without `ContentReadyAt`), `POST /v1/query` (hybrid dense plus BM25 with reciprocal
rank fusion; optional Cohere rerank), `POST /v1/verify` (whitespace-normalized quote-in-text against the block projection),
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
says seven block kinds while the code has eight (`row`). `plans/competitor-partner-data-purpose.md` describes the dormant V2
pipeline. `GeekOAuth`'s `.env.example` names Supabase for a database `AGENTS.md` places on Railway. Integration tests
`GccV2AgentApiContractTests`, `GccV2TaskAgentApiContractTests` and `GccV2SkillApiContractTests` call routes that no
controller has served since 2026-10-03.

## 15. Known limits and open issues

See `plans/real-run-findings-2026-10-07.md` (writer output under its word floors, the lede with no enforced schema, the
JSON-LD name and `@graph` form, the FAQ without evidence) and `plans/single-source-of-responsibility.md` (the plan to divide
the application by single responsibility). The `content_creator_v2` schema and its workers are dormant.
