# Fix: a superseded run's purge failure marks a published run FAILED

Twelve crawl runs are recorded as **failed** while having succeeded. Verbatim
error from one:

```
PATCH /api/geek-crawler/ingest/runs/952b3cc7-90e4-45d6-bcec-d1fc70183f00
-> 502: "Run 952b3cc7-90e4-45d6-bcec-d1fc70183f00 is published, but vectors
for the superseded run 1be09b33-54fd-4a62-81de-462293db9314 could not be
purged. Crawling is stopped until Qdrant deletion succeeds for that run."
```

The message says **"is published"** — the crawl worked and the pages ingested.
What failed was purging a *different*, superseded run's vectors from Qdrant. The
run is nonetheless marked failed and its local data purged, so the crawler UI at
`localhost:3000/runs` lists it under "failed or cancelled".

## These runs are fully indexed

Measured in Qdrant:

| run | host | chunks |
|---|---|---|
| `86cbc5e9` | highradius.com | **42,603** |
| `23eb76a1` | medius.com | **21,488** |
| `952b3cc7` | eojohnson.com | **10,956** |
| `32623e1f` | avidxchange.com | **9,655** |

So "failed" is wrong for every one of them, and the label cost real debugging
time — the operator remembered them completing and could not reconcile that with
the UI.

## Where it happens

```
GeekAPI/Controllers/GeekCrawler/GeekCrawlerIngestController.cs:372   (this message)
and the sibling halts at :146, :152, :354, :365
```

## Root cause of the purge failures, for context

They cluster on **24–25 September**, when `geek_crawler_chunks` was **dropped out
from under a running API**. With no collection, every purge 500ed, and GeekAPI
turned that into a 502 on the PATCH publishing a different, healthy run.

Note what it was *not*: deleting points for a run whose points no longer existed
already succeeded and still does — a Qdrant filter delete matching nothing is not
an error. See the corrected section below; getting these two absences the wrong way
round is what made the first version of this plan propose a fix that would have
prevented none of them.

The missing-collection case was fixed in `Geek-Crawler-Rag@b03e968`. The trigger
was environmental and is now handled — but the **policy** that turned a purge
failure into a failed published run is still live, and that is what this plan is
about.

## The question to settle

This is a design decision, not a bug fix:

> Should a **superseded** run's purge failure mark the **current, published** run
> failed and halt crawling?

Arguments to weigh rather than assume:

- **For halting:** orphaned vectors mean stale content can still be retrieved and
  cited. That is a correctness risk, and silently continuing would be the kind of
  fallback `AGENTS.md` forbids.
- **Against conflating:** the current run succeeded. Marking it failed destroys
  true information, and purging its local data removes the evidence. An operator
  cannot tell "this crawl is bad" from "an unrelated cleanup is pending".

A likely shape, but **validate it**: keep refusing to go quiet about orphaned
vectors, but record that condition against the **superseded** run (or a distinct
operator-visible state such as `awaiting_vector_purge`, per `AGENTS.md`'s rule
that jobs reach `ready`/`failed`/`awaiting_*` and never `pending` forever), while
letting the published run be recorded as published. **Do not simply swallow the
purge failure.**

## The RAG side is already done -- and the diagnosis below was wrong

**Corrected 2026-09-28.** This section used to say: decide whether "there are no
points for this run" should be a success rather than a failure, and that fixing it
"alone would have prevented all twelve". Both halves were wrong, and
`Geek-Crawler-Rag@b03e968` (2026-09-28 13:52, *"stop one job, pause the feed, and
stop lying about a purge"*) records why:

> The plan blamed "no points for this run" -- that case already returns success and
> did then, verified against the live collection, so fixing it would have prevented
> none of them.

Two absences look alike and are not, per `qdrant_store.delete_by_run_id`'s own
docstring:

| absence | behaviour |
|---|---|
| No points match the filter | **Already success.** A Qdrant filter delete matching nothing is not an error. Never needed fixing. |
| **No collection at all** | **This is what raised.** `geek_crawler_chunks` was dropped out from under a running API on 2026-09-24, every purge 500ed, and GeekAPI turned that into a 502 on the PATCH publishing a *different*, healthy run. |

`delete_by_run_id` now treats a missing collection as success -- with no vectors
for the run, that is precisely the state the caller asked for. Everything that is
not a missing collection still raises, because GeekAPI deletes the pages a run's
vectors cite once this reports success.

**So there is nothing to do in Geek-Crawler-Rag.** What remains is only the
GeekBackend question above: whether a superseded run's purge failure should mark
the current, published run failed. That question stands on its own -- the runs on
24-25 September were mislabelled regardless of which absence caused the raise.

## Constraints

From `GeekBackend/AGENTS.md`:

- Jobs reach `ready`, `failed` (+ error), or explicit `awaiting_*` — never
  `pending` forever.
- Spawn/claim failures become `failed` or operator-visible errors, never
  log-only.
- No silent substitution of success when a required step failed.
- Never document a safety property that no check enforces.

## Sequencing

This touches `GeekCrawlerIngestController.cs` near 146–372;
`plans/fix-oversized-links-batch.md` (in this same directory) may touch the same
file near line 664 — only if that plan's "raise the cap" option is chosen over
chunking. Sequence them or expect a merge.

## Do not deploy while indexing is active

A push to `Geek-Crawler-Rag` auto-deploys and recreates the container, which
kills the running index job — that is how the Microsoft run died on 2026-09-28.
Check for a running job first:

```bash
ssh -i ~/.ssh/hostinger_rag_ed25519 root@2.24.101.90 \
  'docker exec -i geek-crawler-rag-api-1 python -' < scripts/list_unindexed_runs.py
```
