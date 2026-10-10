# More sections, so a page reaches its word floor

Written 2026-10-10. **Status: built the same night, not pushed, not yet seen in a real Generate.** Commits `d34d0c7` (sections, floors, the written-so-far block, the run record) and `c6db7ce` (Revise) on branch `cleanup/one-content-creator`. 1,739 unit and 44 integration tests pass. Estimates are marked as such; the real run decides.

## Context

Jeff, 2026-10-10: "Add more sections."

Pages land 300 to 500 words under each batch floor and well under the page floor. The 2026-10-07
run log shows why: 21 body calls, none cut off, each stopping on its own at about 650 words. A
page's length is the number of body calls times that. A pillar and a tool page each make three body
calls, about 2,000 words of body against a 3,000-word floor. More sections means more calls.

Tool gets the same change as Pillar: "Tool ... at very least should equal a Pillar on every measure"
(Jeff, 2026-09-28, recorded in `GccLongFormTypes.GetSeoLengthRules`).

## The review of this plan, checked against the code

The review's fixes name `PrepareBodyInput`, `sectionBatchSize` and a Revise step in the pipeline. All
three are in `ContentGenerationOrchestrator.cs`, the Workflow product. The pages in question were
written by `GccGenerateService`: the message "sections 1-2 is 625 words against a 1,100-word floor"
is its wording. Each concern is answered below in the code that actually ran.

| Review's concern | What the code shows | What this plan does |
|---|---|---|
| A later call repeats an earlier one | True risk. Every call carries the whole evidence and sees the opening, but not the other calls' text | Each call is told what the calls before it wrote (change 4) |
| Slice the evidence per call | Nothing maps a passage to a section. This is the passage-selection work held on 2026-10-07. A guessed split starves some sections, and a starved section writes less | Not done. Stated under "Not in this plan" |
| A 300-word ask makes the writer write less | Correct, and the log supports it: asked 1,000 it wrote 641, asked 1,100 it wrote 674, asked 1,200 it wrote 710. About a third of a word per word asked, on four pages | The ask is not lowered at all. Only the floor a call is checked against changes (change 3) |
| Revise will compress the page to 650 words | Revise is not a step of Generate. Only the Revise button calls it. And it already refuses a revision that comes back a quarter shorter, leaving the page unchanged. The real problem: it rewrites the whole body in one call, so on a long page it is refused every time | Revise writes in the same two-section calls (change 5) |

## The numbers, derived rather than picked

| | Today | After |
|---|---|---|
| Body sections per page | Pillar 5, Tool 6 | 10 each |
| Body calls per page, two sections each | 3 | 5 |
| What each section is asked for | 500 to 700 words | unchanged |
| What a call is checked against | 1,000 to 1,200 words | 600 words |
| Expected body at the measured 650 per call | about 2,000 | about 3,200 |
| Page floor | 3,000 | unchanged |

Ten sections is the count that reaches the page's existing 3,500 target at the measured yield. The
600 is the page floor divided by the number of body calls, computed in code, so the calls' floors
add up to the page floor exactly.

A remainder is at most four words, since it is what is left after dividing by five calls. It is
spread one word at a time across the first calls, so no call owes more than one word above another.
No call's floor is capped: a capped floor would make the calls add up to less than the page floor
and hide a real shortfall. What protects a call from an unreachable floor is a test (see Tests): if
a page floor is ever raised so that one call owes more than a call writes, the test fails and says
the page needs more sections.

## Changes

**1. Pillar outline, 5 body sections to 10** —
`GeekAPI/Services/ContentCreator/ContentTypes/PillarPrompts.cs`. Each existing obligation is split
along what it already says. No new topic.

| Today | Becomes |
|---|---|
| what is going wrong today and what the status quo costs | (1) what is going wrong in this work today · (2) what the status quo costs: hours, errors, delay, risk, and who absorbs them |
| how the approach works end to end | (3) unchanged |
| what separates an implementation that holds up from one that stalls, the early decisions | (4) what separates one that holds up from one that stalls · (5) the decisions made early that cannot be unmade |
| what rolling this out involves: sequence, data, integration, people | (6) the rollout sequence · (7) the data and the integration · (8) the people whose work changes |
| when it is the right call and when not, what to do next, what to expect | (9) when this is the right call and when it is not · (10) what the reader should do next and what to expect |

The operator's framing keeps its places: the failures guide sections 1 and 2, the automation to
pitch guides section 3, and sections 4 to 10 are held to that same approach.

**2. Tool outline, 6 body sections to 10** —
`GeekAPI/Services/ContentCreator/ContentTypes/ToolPrompts.cs`.

| Today | Becomes |
|---|---|
| the opening (operator's framing) | (1) unchanged |
| what the product does, as what it removes from the reader's week | (2) unchanged |
| how it works: mechanics and architecture | (3) its mechanics, step by step · (4) its architecture: what it connects to and what it holds |
| what deploying it involves | (5) what shortens go-live and the order it happens in · (6) the data structure and mapping decisions that matter upfront · (7) what gets configured: approval chains, routing, automation, and its extension mechanism if it has one |
| how a buyer should judge it: fit, pricing model, adjacent approaches | (8) fit and pricing model · (9) the adjacent approaches the reader is also weighing |
| who it suits, who it does not, what to do next | (10) unchanged, so the page's own closing still lands on it |

Each new slot carries the part of the old slot's guidance that applies to it, word for word.

**3. The ask stays, the checked floor becomes true.** `SectionSlot` gains the words a section owes,
separate from the size it is asked for. Pillar and Tool slots are asked for 500 to 700 words as
today and owe the page floor divided across the body. `GccGenerateService.BatchFloorWords` sums what
the slots owe. Blog's slots set nothing new, so Blog is checked exactly as today. Two prompt
sentences are corrected for Pillar and Tool so they state the ask and the call's floor without
contradicting each other: the "your share is about..." line in `SeoBodyInstruction`, and the tool
prompt's "the lower figure is owed".

**4. Each call is told what the page already says, in a few hundred words.**
`GenerateSectionsInBatchesAsync` already holds the sections written so far. The next call's prompt
gets a block beside the existing opening block, under its own clear header. It is not the earlier
text. It is, for each earlier section: its heading, its first sentence, and the figures it already
cited, read by the same grammar the numbers check uses. Plain text built by code, no JSON, no model
call. At the fifth call it is under 400 words, against roughly 40,000 words of evidence already in
the prompt, so it costs nothing in attention or room.

**5. Revise writes in the same two-section calls** — `GccGenerateService.ReviseAsync`. Every call
is shown the whole current page as plain text, read-only, the way Revise shows it today, so it sees
what comes before and after its own sections and can keep the handoffs. It is assigned two sections
and returns those two. That is more context than a sliding window of the neighbours, and it is
cheap: Revise carries no evidence, so the whole page is about 3,500 words per call. A section-scope
Revise sends only the call holding the section the operator named, and every other section is kept
byte for byte. A name that matches no heading on the page is refused, naming the page's headings. The
quarter-shorter refusal stays, on the whole page. A call that returns a different number of sections
than it was given is refused.

**6. The run log says which call of how many.** The `batch` event gains the call's number, the
total, and the floor it was checked against, beside the words it already records. A drop in yield
is then readable call by call.

**7. Nothing else moves.** Two sections per call, no second call after a short one, a shortfall is
still a reported gap, the 3,000 floor stays, Blog is untouched.

## What it costs (estimate, measured only by a real run)

Two more body calls per page. A five-partner run goes from about 40 calls to about 52, most of the
added input served from the provider's cache. Estimated $2.47 to about $3.30 a run, about a minute
longer per page. A Revise becomes five small calls instead of one; it carries no evidence.

## Not in this plan, stated

- **Evidence is not sliced per call.** It needs a passage-to-section mapping that does not exist.
- **The Workflow product's tool path reads the same tool outline** (`ToolPageGenerator.cs:563`).
  `AGENTS.md` records that path as unable to reach its crawl source today. Not changed.
- **Whether sections still repeat each other** is something only the real run shows.

## Tests

- The outline tests assert the exact new lengths: ten body sections for Pillar and for Tool, and
  the framing on exactly sections 1, 2 and 3 with the pointer on 4 to 10.
- The floors add up: for section counts 5 through 11 and floors 1,800, 3,000 and 3,250, the sum of
  the calls' floors equals the page floor, including an odd last call, and no two calls differ by
  more than one word. For Pillar and Tool as shipped, every call's floor is exactly 600.
- No call owes more than a call writes: the measured yield, 650 words, is a named constant with its
  source, and the test fails for any type whose per-call floor exceeds it.
- The `batch` event carries the call number, the total and the floor.
- A slot's ask is unchanged and its owed figure is the derived one; Blog's batch floor is the number
  it was before.
- The written-so-far block names every earlier heading, carries no full paragraph and no JSON, and
  is absent from the first call.
- Revise: five calls for a ten-section page, each shown the whole page; a section-scope Revise makes
  one call and leaves the other sections identical; an unknown section name is refused; a short
  revision is refused.
- `dotnet test GeekBackend.Tests` and the integration tests green.

## Verification

- Build clean, both suites passing.
- Jeff's real run, read from the Run log: five `batch` events per page; each batch's `words` against
  600; the `verdict` word count against 3,000; and whether sections repeat each other. Nothing is
  called fixed before that.

Two commits on the current branch, `cleanup/one-content-creator`: sections, floors and the
written-so-far block; then Revise. Not pushed without Jeff's word.
