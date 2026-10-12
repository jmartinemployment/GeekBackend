# Session notes — 2026-09-26 → 2026-09-28

Everything from the session, in reading order. Current state first.

---

## 1. Current state, right now

| | |
|---|---|
| Qdrant `geek_crawler_chunks` | **0 points** — wiped, collection recreated with correct sparse-vector config |
| Mongo `crawl_runs` | **44 runs**, pages intact |
| `rag_index_jobs` | **empty** — from RAG's view nothing has ever been indexed |
| API container | up, healthy, image built 2026-09-28 19:04 |
| `EMBED_BATCH_SIZE` | 56 |
| `mem_limit` (api) | 7 GiB |

### ⚠️ One thing that will act on its own

A `*/30` crontab entry runs `/docker/geek-crawler-rag/requeue-stranded.sh`. With
the job table empty it will classify **all 44 runs as `NEVER_QUEUED` and requeue
every one of them** on its next tick. That is ~400,000+ chunks, 20+ hours of
indexing.

To stop it: `crontab -e`, delete the `requeue-stranded.sh` line.

Re-indexing everything is one command when you want it:

```bash
ssh -i ~/.ssh/hostinger_rag_ed25519 root@2.24.101.90 \
  'docker exec -i geek-crawler-rag-api-1 python - --requeue' \
  < Geek-Crawler-Rag/scripts/list_unindexed_runs.py
```

Read-only without `--requeue`. Safe to run any time.

---

## 2. What shipped

### Geek-Crawler-Rag (all pushed to `main`)

| commit | what |
|---|---|
| `17ba617` | Re-post runbook + `scripts/list_unindexed_runs.py` |
| `98cd1d9` | Removed a duplicate status webhook (two identical POSTs per batch) |
| `d2eacec` | **The OpenAI retry** + §3a and Cursor-rule amendments |
| `0863544` | Fixed the CI job calling a nonexistent npm script |
| `c100b40` | A refused enqueue no longer passes for an accepted one |
| `272cb7b` | Compose template matched to the box |

Not mine, but landed today and matters: **`b03e968`** — kill-one-job, pause the
scheduler, and the missing-collection purge fix. **Verified deployed.**

### GeekBackend (pushed to `main`)

| commit | what |
|---|---|
| `f1517b9` | Lost-enqueue logging — had been stranded on a branch, never deployed |
| `0eaabc8` | OpenAI stubbed in the contract tests |

### The retry (`d2eacec`), since it was the big one

`LlamaIndexEngine._embed_batch` retries a bounded, logged number of times on three
classes only: connection never opened, connection dropped, provider 5xx. Default 2
retries. **Not retried:** a 400 (our defect), a 429 (the throttle's job), Mongo,
Qdrant, GeekAPI ingest. Terminal behaviour unchanged — spent attempts still
quarantine or fail with the real error. `OPENAI_EMBEDDING_TRANSIENT_RETRIES=0`
restores the old behaviour exactly.

Both rule files were amended to legalise it, because leaving code that contradicts
an `alwaysApply: true` Cursor rule would just get reverted by the next session.

---

## 3. The batch-size experiment — the answer is "it doesn't matter"

| batch | baseline | **measured peak** | % of 7 GiB |
|---|---|---|---|
| 32 | ~2.35 GiB | — | — |
| 56 | 3.14–3.45 | **6.20** | 89% |
| 64 | 3.50–3.64 | **6.24** | 89% |
| 128 | — | **>7 → OOM-killed** | — |

**A 12.5% cut in batch size bought 0.04 GiB — nothing.** 56 and 64 are
indistinguishable at the peak.

**Why:** the flush is a *minimum, not a cap*. `indexer.py` does
`if len(pending) >= embed_batch_size:` and then sends **all** of `pending`, so a
page's worth of chunks lands on top of an almost-full batch. Effective batch is
`batch_size + chunks_per_page`, and your sites run 37–58 chunks/page. The setting
moves the floor, not the ceiling.

**Consequence:** dropping to 48 or 32 would not help either. My earlier table
projecting 3.7 GiB at 32 was wrong. The only change that would move memory is
**slicing `pending` into exact `batch_size` batches** — a real fix, in
Geek-Crawler-Rag, not yet written up.

Throughput is ~305 chunks/min regardless. 128 OOM-killed the container
(`docker events`: `oom` then `die exitCode=137` — note `docker inspect` reports
`OOMKilled=false` afterwards, so the event log is the authority).

---

## 4. The "shit load of failed crawls" — 16 of 17 are mislabelled

Source: `localhost:3000/runs`, backed by `/api/crawls/failures`. **None are crawl
failures.** Every error is an `/api/geek-crawler/ingest/...` URL, meaning the pages
were fetched and extracted fine and the *handoff* failed.

| count | error | reality |
|---|---|---|
| 12 | `PATCH …/runs/{id} → 502 "is published, but vectors for the superseded run … could not be purged"` | **Succeeded.** `86cbc5e9` = 42,603 chunks, `23eb76a1` = 21,488, `952b3cc7` = 10,956, `32623e1f` = 9,655 |
| 4 | `pages/batch → 500`, `transport: fetch failed` | transient; ramp.com later indexed fine |
| 1 | `links/batch size 5779 exceeds atomic max 2000` | **a real bug** — netsuite.com lost its crawl |

Also `extractEmpty: 153` pages yielded no content, and every row has
`purgedAtUtc` set — which is why Mongo showed zero failed runs and why the run
count kept dropping. The purge works as designed; it just removes the evidence.

**Ingest, in plain terms:** crawl (your machine fetches) → **ingest** (your machine
POSTs to GeekAPI → Mongo) → index (RAG embeds → Qdrant). All 17 failures are in the
middle stage.

---

## 5. Open decisions

1. **Re-index the 44 runs?** All, selectively, or fix the flush cap first. Nothing
   is queued, so now is the clean window for config or code changes.
2. **Disable the cron** if you don't want all 44 indexing unattended.
3. ~~**The oversized links batch.**~~ **Done.** `MAX_LINKS_PER_BATCH` is 10,000 on
   both sides, GeekAPI reads it from `GeekCrawlerIngestLimits` instead of a bare
   `2000`, and the crawler enforces `MAX_BATCH_BODY_BYTES` as well as the count. The
   plan file is deleted — it also carried the wrong premise that links go to Postgres.
4. ~~**The superseded-run purge halt.**~~ **Done**, and settled the way that plan
   suggested: the superseded run takes `AwaitingVectorPurge`, the published run stays
   published, and it is reported on the response as
   `supersededRunAwaitingVectorPurge`. Crawling still blocks for the owner until the
   purge succeeds, so nothing is swallowed. Plan file deleted.
5. ~~*(removed item)*~~ **Done 2026-09-29.**
6. **The flush cap** — the only change that would actually move memory. Not yet
   written up.

---

## 6. Things I got wrong, corrected

Listed because several are recorded in the plans and code comments, and because
the pattern is worth knowing: every one was an inference I stated before checking.

- **"Batch 128 will be roughly fine"** — it OOM-killed the container. Cost ~2 hours
  and a stranded queue.
- **"Memory plateaued at 64"** — called on two data points; it went on to 6.24.
- **"48 will peak near 4.9 GiB"** — linear model, wrong; 56 came in at 6.20.
- **"The heartbeat is dead"** — I said this repeatedly and called it the
  highest-value bug. It works: measured `leaseUntil − claimedAtUtc = 4584s` on a
  live job. I had only ever looked at jobs whose *process* was already dead.
- **"Fixing RAG's 'no points' delete would have prevented all twelve"** — wrong,
  and `b03e968` says so explicitly: that case always returned success. The actual
  cause was a *missing collection*. Corrected in the plan.
- **"This is the densest run you've ever indexed"** — computed from 74 pages of a
  736-page run. It was the same tipalti.com content already indexed at 37
  chunks/page. You caught it from memory.
- **"OOMKilled=false, so not an OOM"** — `docker inspect` reports post-restart
  state; the event log had `oom`.
- **Pushing during indexing** — I flagged the deploy-vs-crawl race and then pushed
  into it, killing the Microsoft run.

---

## 7. Reference

```bash
# status, read-only, safe any time
ssh -i ~/.ssh/hostinger_rag_ed25519 root@2.24.101.90 \
  'docker exec -i geek-crawler-rag-api-1 python - ' \
  < Geek-Crawler-Rag/scripts/list_unindexed_runs.py

# cron log
ssh -i ~/.ssh/hostinger_rag_ed25519 root@2.24.101.90 'tail -40 /var/log/rag-requeue.log'

# new controls from b03e968 (deployed)
POST /v1/index/{runId}/kill              # stop one job, keep its vectors
POST /v1/index-scheduler/pause|resume    # durable, survives a container recreate
```

**A hung job is not a stranded job.** Tell them apart by whether `leaseUntil`
advances; a hang needs `docker restart` (which keeps the model cache) before a
re-post. Re-posting a hung job deadlocks on the shared embedding lock. Full
procedure: `Geek-Crawler-Rag/docs/index-job-recovery-after-restart.md`.

**Don't push to Geek-Crawler-Rag while indexing** — it auto-deploys and recreates
the container, killing the running job.
