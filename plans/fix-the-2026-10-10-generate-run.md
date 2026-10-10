# The 29 gaps of the 2026-10-10 run, and the outside review

Written 2026-10-10. **Status: approved by Jeff on 2026-10-10. Changes 1 to 6 are on `main`, one
commit each (`3c135dc` to `d06b18e`), pushed 2026-10-10 on his word; their tests pass (1,805 unit,
44 integration). Not yet seen in a real run. Change 7 is not built: its model trial failed, see "The
model trial". Change 8 goes with it.** The findings are read from the
run's own log (132 events, read through the app) and from the code. Where something is a reading and
not a reading of the log, it says so.

## Context

The 12:14 UTC run of 2026-10-10 on "Cash Flow Forecasting: Automated Accounts Receivable" wrote all
seven pages and listed 29 gaps. Jeff pasted the list, then an outside review in pieces: prompt
changes, a LlamaIndex fix (of which he said "Less the retry Loop"), and search tuning.

| Lines | What | On which pages |
|---|---|---|
| 17 | one call's word count under its floor | tool 12, blog 3, pillar 2 |
| 4 | keyword at 0.36 to 0.37% against 0.4% | Chaserhq, Invoiced, Versapay, Bill |
| 6 | an FAQ question shown 3 passages and not answered | Bill 5, Chaserhq 1 |
| 1 | no block quotation | Bill |
| 1 | no keyword heading in the first call | pillar |

## What the log shows

**No page is short and no call was cut off.** Pillar 3,728 words; tool pages 3,480 to 4,436; blog
2,633. All 64 model calls ended on their own (`finishReason: stop`), using about 1,000 of 16,384
output tokens. The 30 pillar and tool body calls wrote 504 to 720 words, median 617, each told to
aim for about 1,200. So the 17 length lines are report noise.

**Keyword: the step stopped short of its own count.**

| Page | Aim | Before | After | Page density |
|---|---|---|---|---|
| Chaserhq | 20 | 8 | 16 | 0.37% |
| Invoiced | 19 | 8 | 13 | 0.37% |
| Versapay | 19 | 6 | 14 | 0.37% |
| Bill | 21 | 6 | 15 | 0.36% |
| Upflow | 21 | 10 | 19 | 0.42% passes |
| Pillar | 20 | 19 | 20 | 0.53% passes |
| Blog | 14 | 10 | 12 | 0.45% passes |

Each failing page is two uses short of the mark. Each has 6 to 16 paragraphs that say "accounts
receivable" without "automated", several in sections that had already met their share and so were
left alone. No edit produced nonsense: "manual automated" appears in no paragraph.

**FAQ: 30 of 36 questions answered. Four of the six misses are the crawl's. The other two are
not settled: see the correction under this list.**
- Bill, five misses. The three passages shown for them came from a terms-of-service page, an
  engineering post about a design system, a page-template stub (`/listicle`), a travel-spend post
  and "Should you hire an accountant". The writer was right to leave them out.
- The one description of BILL's cash-flow forecasting this run retrieved is a paragraph on its
  Product Updates page: forecasts from historical data, "what if" simulations, custom views and
  dashboards. In the FAQ call it sits under the first question, which was answered from it. I read
  it as answering two of the missed ones ("test the impact of hiring", "custom visualizations"), for
  which it was not returned. Nothing retrieved says how far out the forecast runs or whether it
  splits payment types.
- Checked against the crawl itself: bill.com's crawl holds 599 pages. Its one page on the subject,
  "BILL Insights & Forecasting for firms", is 439 characters of text and answers none of them. No
  cash-flow forecasting product page is among the 39 product pages crawled. So three of Bill's
  questions cannot be answered from this crawl by any search.
- Chaserhq, one miss. The right page was found, Chaser's revenue forecast page. It does not say
  whether a forecast can be edited by hand. Chaser's crawl (275 pages) has two more forecast pages,
  `/cash-flow-forecast` and `/receivables-forecast`, that the search did not return.

**Correction, 2026-10-10, after the model trial.** I wrote that the search cost Bill those two
answers. The paragraph was in the same model call: Bill's eight questions are one call (event 124),
and the paragraph is printed in it under "Found for Q1". The prompt does not tie a question to its
own group (`ContentPromptBuilder.cs:1960-1964`: answer only from the partner evidence, leave a
question out if no passage answers it, state no capability the evidence does not state). The writer
answered the first question from the paragraph and left the other two out. The paragraph says "what
if" simulations and customised views and dashboards; it does not say hiring, purchases or expense
tracking. So "it answers them" was my reading, and the writer's was the stricter one. Whether the
writer would have answered with the paragraph under the question's own heading is not known.

**Quotation: Bill's 40 sentences came from four pages, none about receivables.** Twelve from the
`/listicle` stub (placeholder text and competitors' card prices), twelve from a payables case study,
twelve from a payables post, four from the payables product page. BILL's accounts receivable product
page was retrieved, sixth in order, and gave none: the list was full.

**Not in the gap list: every tool page has a heading that reads "Manual Automated Accounts
Receivable".** "The Challenges of Manual Automated Accounts Receivable" on Upflow, Invoiced and Bill;
"The Cost(s) of Manual Automated Accounts Receivable" on Chaserhq and Versapay. And the pillar's "no
keyword heading" line is a false alarm: the pillar has eight H2s with the phrase.

**Links placed by code worked on their first real run:** five on the pillar, five on the blog, one a
partner, none missed.

## The review, checked

**Prompt changes, none taken up**

| The review says | What the code and the log say |
|---|---|
| Keyword once per 300 words; require it in the FAQ | A count in the prompt, ruled out 2026-10-10. Once per 300 words is 0.33% against 0.4% |
| Aim for 650 to 700; tell the blog 1,000 | Each call is already told about 1,200 (`ContentPromptBuilder.cs:499`) and writes a median 617. No page is short |
| At least three sub-points, a step-by-step example | A required example with nothing behind it gets made up (`:2698` says the opposite on purpose) |
| Select the closest span as the quotation | Bill's 40 held nothing about receivables. The closest would have been a payables sentence |
| Explain how the platform generally approaches it; override the refine template | No refine step exists. A deduced answer states things about a partner's product its pages do not |
| Pass a fixed heading into the pillar | The pillar has eight keyword headings. One fixed string is the same heading on every pillar |
| Custom QA template on the ResponseSynthesizer | There is none. The Library writes nothing |
| A validation and retry loop | Left out on Jeff's word. See "The two loops the review named" |

**Search changes**

| The review says | What the code and the log say |
|---|---|
| A reranker and a similarity cut-off | **Tried without Cohere, and it does not do the job** ("The model trial"). The Library's only reranker is a call to Cohere, a paid outside service nobody signed up for, so it has never run (`Geek-Crawler-Rag` `rerank.py:53`, `HANDOFF.md:269`). No cut-off exists. See "Reranking with no outside service" |
| Dense hits mask BM25; raise the keyword weight (`alpha`) | That was the old fusion, replaced 2026-10-08. The halves now count by rank alone and `alpha` is not read (`llama_engine.py:81-85`). The log points the other way: "impact of hiring" brought back "hire an accountant", which reads like a word match. Which half returned it is not verified |
| Check the 200-token child and 1,000-token parent | The child is 200 (`config.py:82`). The parent is 500, down from 1,000, because the embedder cuts at 512 (`:86-101`) |
| Tag nodes and filter on the tags | Host, company, category, intent and section title are already tagged and filtered (`metadata.py`, `llama_engine.py:672`). A testimonial tag feeds the quotation ruled out on 2026-10-01 |
| Re-chunk so quotations are found | Quotations are not found by the search. GeekAPI cuts them from the pages' own blocks (`GccQuoteCandidates.cs:66-98`) |

**The two loops the review named.** Neither is built, and the code has neither.

| Loop | What it does | Cost |
|---|---|---|
| Retry (its item 4, "Validation & Retry Loop") | Code checks what the model wrote for word count and keyword. If a check fails the model is called again with the same job plus "an aggressive correction prompt", and checked again. The review gives no limit on how many times | One more paid model call per failed check, each time round |
| Refine (its item 3, and "a dedicated Refine/Retry loop") | Not a retry of a failure. LlamaIndex's way of answering from several passages: the model answers from the first, then is called again for each further passage to improve that answer | One model call per passage instead of one per answer |

A retry would not have helped this run: the 17 length lines were false alarms, the keyword miss was
the code step stopping early, and the FAQ gaps came from the search and the crawl. The model is
called once per job (`OpenAiCompatibleOutcome.cs:12`), and a short call is reported, not written
again (Jeff, 2026-10-06).

## The review of this plan, and its fastembed setup, checked

The review agreed with the plan and added C# sketches and a Python setup. Neither is this code.

| It says | Checked |
|---|---|
| `GccKeywordRemap : ICcwPostProcessor`, `Apply(PageDraft, KeywordTarget)`, `InjectKeywordValue(maxInjections: 2)` | None of those types exist. The real step is `GccKeywordRemap.Apply(ContentDocument, keyword)`. It never inserts the phrase: it puts back a word the writer left out, where the grammar allows, one edit a paragraph. Inserting is what Jeff's ruling of 2026-10-10 excludes |
| Skip any section whose text contains "Manual" | The real rule already refuses "manual accounts receivable" at the phrase itself. Skipping whole sections throws away legal places |
| `BuildContextForCall(CallGroup)` with the keyword and section names typed into the prompt builder | No such method. The keyword is the project's; the owning section is a flag on the slot (`SectionSlot.OwnsKeywordHeading`), set by the type that owns the outline |
| `BAAI/bge-reranker-base`, "roughly 80 MB", under a second | That model is 1.04 GB in the installed library. The 80 MB one is `Xenova/ms-marco-MiniLM-L-6-v2` (`fastembed/rerank/cross_encoder/onnx_text_cross_encoder.py`) |
| `from fastembed.rerank import TextReranker`; `model.rerank(pairs)` | The class is `fastembed.rerank.cross_encoder.TextCrossEncoder`; `rerank(query, documents)` takes the question and the passages |
| `GroundedPassage`, `request.intent == "FAQ"`, `request.query`, `execute_hybrid_vector_search` | None exist in the Library. The seam is `Reranker.rerank(query, documents, top_n)` returning `RerankOutcome` (`rerank.py`), called at `query.py:179`, built at `app.py:173` |
| `raise RuntimeError(...)` on failure | No exceptions, by the workspace rule. The code already returns a failed outcome and the search answers with an error and no passages (`query.py:182`). Kept |
| Load the model on the first request; run it in the request | The first FAQ search of a Generate would wait on a download, and inference on the event loop stalls the whole service (`local_embedding.py:14-17`). Loaded at start; run through `asyncio.to_thread`, as the embedder is |
| Set `FASTEMBED_CACHE_PATH="/workspace/cache/fastembed"` | Already `/tmp/fastembed_cache`, where the named volume is mounted, pinned on purpose (`deploy/hostinger-compose.yml:44-47`, `:73`). Changing it re-downloads on every restart |
| `return candidates[:3]` | Three is one caller's number. The search's own `top_k` and the page-diverse selection stay (`query.py:199`) |

## Reranking with no outside service

Jeff, 2026-10-10: Qdrant has its own reranking (late interaction, rescoring and oversampling, score
boosting by formula), and "If there are Gaps reported, we will be right back here again."

| Mechanism | On this box |
|---|---|
| Late interaction (ColBERT multi-vectors) | A real reranker, and Qdrant v1.13.4 on the box supports it. It stores a vector per word for every passage. bill.com's run alone is 11,720 passages and Ramp's about 28,000 (`HANDOFF.md:418`), so it means re-embedding every run and, by my estimate, tens of GB of storage |
| Rescoring and oversampling | Does not apply. It recovers accuracy lost to compressed vectors; this collection is not compressed (`qdrant_store.py:109`), and it does not judge whether a passage answers a question |
| Score boosting by formula | Needs Qdrant 1.14; the box runs 1.13.4. It raises or lowers a passage by its tags, so it could push legal pages down. It does not read the passage against the question |

**What was proposed, with no key, no account and no re-index:** the reranking models that ship in
`fastembed` 0.8.1, already installed in the Library (`fastembed.rerank`; the smallest is 80 MB), run
inside the Library on a search's candidates, in the place the Cohere call sits today. **Tried on
this run's questions and passages on 2026-10-10, and none of them does the job: see "The model
trial".**

**The box's size is not verified.** An earlier version of this file said "a 2-CPU, 8 GB machine",
from the Library's `architecture.md:21`. Its README measured the host at 16 GB on 2026-09-25, and
the compose file gives the API container 3.5 CPUs and 7 GB. Not measured: change 7 was not built.

## Causes

**Keyword.** Each section may take only its own share, and a section with no legal place loses it
(`GccKeywordRemap.cs:139-154`).

**Length.** A call under its floor is reported whatever the page's total (`GccGenerateService.cs:3496`).

**Headings. Caused by the ten-section change of 2026-10-10.** With no section named, the first call
is asked for an H2 holding the exact phrase (`SectionSlot.cs:69`, `ContentPromptBuilder.cs:508`).
- On a tool page the first call's first section is the problem done by hand, so the writer put the
  phrase there as well as on the next section.
- On the pillar, splitting into ten sections left the first call with two by-hand sections
  (`PillarPrompts.cs:43-47`); the writer rightly wrote no keyword heading there and was reported.
- The blog names its section (`BlogPrompts.cs:102`) and has neither fault. No test covers the others.

**Quotation.** The first 12 sentences of each page, in the order pages came back, until 40
(`GccQuoteCandidates.cs:66-98`). Up to 32 pages are read (`GccTypedPassageReader.cs:39`); four fill
the list. The limit was written for about eight pages.

**FAQ search.** The top three of an unranked fusion, with the whole question as the keyword half
(`llama_engine.py:648`), so every common word in it is a search term.

**Report.** The keyword and quotation lines do not name their page. The run log does not record
whether a search was ranked or a passage's score (`HttpGeekCrawlerRagClient.cs:156`, `:168`).

## Changes in GeekBackend

**1. A share a section cannot take goes to the sections that can.** `Guardrail/GccKeywordRemap.cs`,
`Apply`. Unchanged: one edit a paragraph, never a heading, a quotation, a linked run or a paragraph
that already has the phrase, never above the count. The `keyword` event also records the places each
section offered.

**2. Length is judged once, by the page.** `Guardrail/GccDraftGuard.cs`: one line when the finished
page is under its floor. `GccGenerateService.cs` (`:3022`, `:3371`, `:3074`): a call's word
shortfall is listed only beside that line. No prompt is touched.

**3. The keyword's heading is asked of the section where it reads true, and judged once by the page.**
- `ContentTypes/PillarPrompts.cs`: "how the approach works" owns it. `ContentTypes/ToolPrompts.cs`:
  "what the product actually does" owns it.
- `ContentPromptBuilder.cs:508`: the call that holds that section is told which section's heading
  carries the phrase; its other headings need it only where it is the natural wording.
- `GccGenerateService.cs:3485`: the per-call heading line goes. `GccDraftGuard`: one line when no H2
  on the page carries the phrase (the score's own check, `GcwSeoAnalyzer.cs:91`), and one naming any
  heading that puts "Manual" in front of a keyword beginning "Automated". Reported, not refused.

**4. Every line names its page.** `GccGenerateService.cs:3072`.

**5. The run log says whether each search was ranked, and each FAQ passage's score.**
`GccGenerationCoordinator.cs:957` and the `faq` event.

**6. Quotable sentences come from every page returned.** `GccQuoteCandidates.cs`, `From`: the 40 are
shared across the pages, each giving its first sentences up to its share, a share a page cannot fill
passing on. Still 40, still cut by code, still chosen by number. The brief-time check reads the same
list (`GccAngleQuoteProbe.cs:179`).

`AGENTS.md` and `docs/technical-specification.md` are corrected in the same commits.

## Changes in the Library

**7. FAQ searches reranked by a model inside the Library. Not built: the trial failed.**

The plan's condition was: the two small models are run on this run's real questions and passages,
the one that puts the right paragraph first is the default, and if neither does the change stops.
Neither does. The Library is untouched; its Cohere code is still there, with no key, never run.

### The model trial

What was ranked: Bill's eight FAQ questions, each against the 22 different passages the run's FAQ
call showed for them (event 124, the text printed as "Specific detail", which is what the Library
hands a reranker, `query.py:302-313`). The right paragraph is the Product Updates one. Run on an
Apple M4 with the Library's own `fastembed` 0.8.1, not on the box. All five models the library ships
under a licence this product can use were run; the sixth is non-commercial.

| Model | Size | "test the impact of hiring": right paragraph ranked | "custom visualizations": right paragraph ranked | 40 passages | Peak memory |
|---|---|---|---|---|---|
| `Xenova/ms-marco-MiniLM-L-6-v2` | 0.08 GB | 3rd | 4th | 0.7 s | 2.1 GB |
| `Xenova/ms-marco-MiniLM-L-12-v2` | 0.12 GB | 3rd | 3rd | 1.3 s | 2.2 GB |
| `jinaai/jina-reranker-v1-tiny-en` | 0.13 GB | 3rd | 11th | 0.5 s | 1.5 GB |
| `jinaai/jina-reranker-v1-turbo-en` | 0.15 GB | 7th | 6th | 0.7 s | 1.4 GB |
| `BAAI/bge-reranker-base` (the review's) | 1.04 GB | 7th | 10th | 3.5 s | 2.2 GB |

- **What they put first instead.** For "test the impact of hiring", "Should you hire an accountant:
  buying another business" (both MiniLM models) or the 147-character "Insights & Forecasting" teaser.
  For "custom visualizations", the pricing table or the travel page. The same pages the unranked
  search returned. `bge-reranker-base` put the `/listicle` stub first for six of the eight questions.
- **My reading: they rank on shared words.** All five put the paragraph first for "What is BILL Cash Flow Auto
  Forecasting?", which shares its words. None connects "test the impact of hiring" with "what if
  simulations".
- **The chunk is not the reason.** With the passage cut down to the forecasting sentences alone, the
  two MiniLM models rank it 2nd for both questions, `jina tiny` 1st and 10th, `jina turbo` 5th and
  2nd, `bge` 4th and 10th. None is first for both.
- **A cut-off on the score would cut the right paragraph too.** The two MiniLM models score it -7
  to -8 on the first of those questions and -10 or lower on the second, below the junk they rank
  above it. So change 8 has nothing to stand on.
- **Memory.** Ranking 40 passages in one batch peaked at 1.4 to 2.2 GB on the Mac (0.6 GB in batches
  of eight, measured on one model). The container is given 7 GB and also holds the embedder.
- **What the trial cannot say.** The real candidates for a question are the 40 the fusion ranks best
  for that question. The trial's 22 were gathered by eight different searches, so most are easy to
  beat; "3rd of 22" is the kind end of what the box would see. Whether the paragraph is among the
  40 for those two questions is not verified.

The trial's script and data are not in the repository.

**8. The cut-off. Not built,** for the reason above.

## Not changed

- No prompt gains a count or a length. No FAQ or quotation rule is loosened. No second model call
  after a failed check.
- No re-chunking, no re-index, no new tags, no fusion weights, no Qdrant upgrade, no Cohere.
- **Four questions no code can answer.** Three of Bill's (historical averages instead of the AI
  models; how far out the forecast runs; whether the dashboard splits payment types): nothing in
  bill.com's 599 crawled pages says. One of Chaserhq's (editing the forecast by hand): the page
  found does not say; Chaser's crawl has two more forecast pages the search did not return. A
  question the partner's pages do not answer is listed on every run until it is taken out of the
  brief or a page that answers it is crawled. BILL's forecasting detail is most likely on its help
  centre, which is not in the crawl. Jeff's call which.
- **Junk in bill.com's crawl,** seen and left: the `/listicle` template stub, nine legal pages and an
  engineering post are all in the evidence. Change 6 stops one page taking 12 quotation slots.
  Nothing pushes the rest down: the models tried rank them as high as the search does. Removing
  them from the crawl is separate work.
- **Bill's other two questions** ("test the impact of hiring or major purchases", "custom
  visualizations for non-standard expense tracking"). The crawl's one paragraph on forecasting says
  "what if" simulations and customised views and dashboards, and the writer, shown it, did not
  answer either question from it. They are listed until they are reworded to what BILL's page says,
  taken out, or a page that answers them is crawled. Jeff's call.

## Order

1. Changes 1 to 6: done, one commit each on `main`, pushed 2026-10-10.
2. A real run. No re-index is needed for it: the five partner crawls were crawled and indexed on
   2026-10-09, after the Library's indexing change of 2026-10-08 (`b14c200`, stub chunks not
   indexed), and nothing since has touched the index.

Changes 7 and 8 are not in the order any more.

## What the next run should list

Bill's five questions and Chaserhq's one, and nothing else if each page has two more places for the
phrase. Six lines instead of 29. No code in this plan removes those six: they go when the questions
are reworded or leave the brief, or when a page that answers them is crawled.

## Verification

1. New and changed tests:
   - a section with no place gives its share to one that has a place (`GccKeywordRemapTests.cs`);
   - a call under its floor is listed only when the page is under its floor
     (`MoreSectionsReachTheFloorTests.cs`, `GccDraftGuardTests.cs`);
   - on a pillar and on a tool page, the owning section is named to its call and no other call is
     asked; a page with a keyword H2 lists no heading line; "Manual Automated ..." in a heading is
     listed (`GccKeywordHeadingOwnerTests.cs`, `GccDraftGuardTests.cs`);
   - a tool page's keyword line names the tool;
   - the `grounding` and `faq` events carry the ranked flag and the scores;
   - thirty-two pages each give a quotable sentence before any page gives a second.
2. `dotnet test` on the unit and integration projects: 1,805 and 44 pass with changes 1 to 6
   (1,774 and 44 before them).
3. A real run: the gap list, the tool pages' headings, Bill's quotation, each page's `keyword`
   event and each FAQ question's scores.
