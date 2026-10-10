> **Superseded 2026-10-07 by `single-source-of-responsibility.md`**, which is the full plan (the division, the code map,
> the 47 type consultations, the order of work). Kept for the discussion it records; where they differ, the new file wins.

# Single responsibility: one agent, one job

Written 2026-10-07 and rewritten twice after Jeff's corrections. **The primary goal is Single Source of
Responsibility, and the division is Jeff's:** one Plan/Research agent, one Writer per content type, one Validate agent,
one Save agent. **Status: agreed in principle; nothing is built.** The gate before anything is built is a real, narrow
Generate of the current code (Pillar alone), because none of the 2026-10-07 patches has run against the real model.

## 1. Why

- **One concept in many copies.** A content type is declared in at least six unconnected places (the disabled list, the
  two grounding tables, the coordinator's generator switch, the prompt registry, the export switches, and the frontend's
  hand copy of the disabled list). `GccGroundingResolver` records the week one edit stopped pillar and blog fetching
  any evidence.
- **One lump.** `GccGenerateService.cs` 3,798 lines, `ContentPromptBuilder.cs` 3,444 lines (shared with the Workflow
  product), `GccDraftGuard` shared by every type. The per-type prompt sets are 623 lines of wrappers.
- **The 2026-10-07 refusals were our own rules.** Each of four refused pieces failed exactly one check, and each check
  traced to our own instruction or rule. Everything else passed. See `fix-the-2026-10-07-generate-run.md`.
- A rewrite alone did not fix it, and patching shared code verified by scripted tests is the same thing again.

## 2. The division (Jeff's list)

| # | Agent | One job |
|---|---|---|
| 1 | **Plan / Research** | Read the brief and the three sources (Project site, Partners, Competitors); give each writer its slots and the evidence it needs |
| 2 | **Pillar Write** | Write the pillar page |
| 3 | **Blog Write** | Write the blog post |
| 4 | **Tool Write** | Write one tool page per partner |
| 5, 6, 7 ... | **Other long-form writers** | Comparison, Alternatives, Case Study, How-to Guide, Service Page, Whitepaper, Landing Page. Each stays disabled until it has an approved plan (existing rule) |
| 8, 9, 10 | **Short-form writers** | Email (cold outreach), Social, Image prompt |
| 11 | **Validate / Check** | Run the guards on a finished page and return findings. Never repairs |
| 12 | **Save** | Persist the pieces |

**A writer owns everything about its own type:** its prompts, its slots, its closing and FAQ, its metadata and image
prompts. It does not retrieve, validate, save, or know any other writer. The coordinator that runs the agents for a
request (delete first, keep what wrote, settle) is not in the list and stays plain code, unchanged. *Jeff to confirm.*

**Responsibility at three levels:** the agent; the call (only its own slots); the evidence (decided by Plan/Research and
never by the writer). Evidence has one source, retrieved once per Generate and held in the running job's memory; the
checks read all of it; each call gets a view (`evidence.For(slot)`, whole set first, selection later).

## 3. The three rules (Jeff agreed, 2026-10-07)

1. **Isolation enforced by the build.** A writer cannot reach into another writer's code, and no writer reaches past the
   shared kernel and the Plan/Research, Validate and Save agents. A test fails if one does.
2. **A real run gates each piece**, not scripted tests. Jeff runs it, reads the Run log, then we go on. Nothing counts as
   fixed until that run.
3. **The old code stays out of the way, and nothing falls back to it.** A new agent is built beside the old, with no
   automatic fallback. A setting Jeff controls picks which runs. Once the new one passes a real run, the old code goes.

## 4. Standing rules every agent obeys

No retries and no second model call after a failed check; fail closed (a refused piece costs one page, never the run);
the model returns content, never markup; RAG is retrieval and verification only; OpenAI is the live provider; guards
read the whole evidence set; every call is recorded in the run log; the closing is built by code (`GccClosing`).

**Reasoned repair (proposed; Jeff's position 2026-10-07: "if it reasons ... that is different than just try it again").**
A blind retry stays forbidden. A repair is one attempt where the agent that owns the page reads the Validate agent's finding,
works out what in the input caused it, and changes the input. Limits proposed, not yet confirmed: one attempt per page;
only the failing section; a cost cap; every attempt in the run log; the same checks on the result; the page refused if
the repair fails too; no repair by code editing the text. `CLAUDE.md` section 2 forbids secondary loops, so it needs
Jeff's change before any of this is built.

## 5. What is shared

The kernel only: the recording provider, `GccRunLog`, the typed records passed between agents, rule constants (one
definition per rule, for example `MaxLinkWords`), and `GccClosing`. The Workflow product keeps `ContentPromptBuilder`;
the new agents do not call it.

## 6. The switch

A setting per writer (appsettings or a Railway variable, Jeff's choice): **enabled**, and **implementation**
(`legacy` or `new`). Manual; no automatic fallback. The disabled list is a constant in `GccGenerateService` today.

## 7. Order, and what passes

1. **Gate, now: Jeff runs Pillar alone on the current code** and sends the Run log events. Query:
   `SELECT at, kind, piece, payload_json FROM content_creator.gcc_generate_job_events WHERE at >= '<run start>' AND kind IN ('verdict','outcome','settled','fault','failure','warning','completed') ORDER BY job_id, seq;`
2. **Pillar Write**, new, beside the old. Plan/Research, Validate and Save are extracted from the old code as the pillar
   needs them. Passes when a real Generate shows a clean verdict, the page ending on its closing, no refusal, and a cost
   near $0.40. Then the old pillar code is deleted.
3. **Tool Write** (per-partner fan-out and extraction), then **Blog Write**, then the **short-form writers**.

Between slices nothing else changes. A slice can be the last; the system works at every step.

## 8. Risks

- Moving a live path without a model-side safety net. Per slice: a pinning test through the real orchestrator with a
  scripted model, and a real run. Neither is perfect.
- The Workflow product shares `ContentPromptBuilder` and is left alone.
- The dormant `content_creator_v2` agent tables (about thirty, no live caller, never ran end to end) are not built on.
  Listing them for deletion is a separate decision.

## 9. To settle with Jeff

1. The coordinator stays plain code and outside the list?
2. The setting in `appsettings.json` or a Railway variable?
3. The frontend's disabled list: moves to one backend definition in the same round, or after?
4. Reasoned repair: confirm the limits above, and change `CLAUDE.md` section 2 if yes.
5. The Project-site and Competitor definitions (`docs/content-creator-sources.md`): pending Jeff's words.
