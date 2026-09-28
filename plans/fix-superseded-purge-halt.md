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

They cluster on **24–25 September**, when the Qdrant collection was dropped and
recreated. Deleting points for a run whose points no longer existed failed, and
that halted crawling. So the trigger was environmental — but the **policy** that
turned it into "failed" is still live and will do this again.

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

## Also consider the RAG side

Geek-Crawler-Rag exposes `DELETE /v1/index/runs/{run_id}`. Decide whether "there
are no points for this run" should be a **success** (idempotent delete) rather
than a failure. If it should, that alone would have prevented all twelve, and the
fix belongs in `Geek-Crawler-Rag/src/geek_crawler_rag/app.py`'s
`delete_run_index` and its store call. `tests/test_delete_run_index.py` covers
that route today.

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
`Geek-Crawler-v2/plans/fix-oversized-links-batch.md` may touch the same file near
line 664. Sequence them or expect a merge.

## Do not deploy while indexing is active

A push to `Geek-Crawler-Rag` auto-deploys and recreates the container, which
kills the running index job — that is how the Microsoft run died on 2026-09-28.
Check for a running job first:

```bash
ssh -i ~/.ssh/hostinger_rag_ed25519 root@2.24.101.90 \
  'docker exec -i geek-crawler-rag-api-1 python -' < scripts/list_unindexed_runs.py
```
