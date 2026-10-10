# The keyword: no count for the writer, the exact phrase put back by code, judged on the page

Status: built 2026-10-10 on `cleanup/one-content-creator`, not pushed, not seen in a real run.

## Context

Jeff's item 3, 2026-10-10: sections use the keyword 1 or 3 times when they owe 6 or 7.

Jeff's direction, the same day, verbatim: "Keyword Stuff remap: Do not rely on the LLM to count its
own keyword usage to hit a target of 6 or 7 mentions. Let the LLM write naturally, and write a light
post-processing utility that replaces natural semantic variations with the strict required target
phrase where grammatically appropriate."

This replaced an earlier plan that would have stated a count per section in the prompt. That plan's
page-level judgement and run-log fields survive here.

The 2026-10-07 run log, keyword "Automated Approval Workflows":

| | |
|---|---|
| Body calls warned short on the keyword | 18 of 21 |
| Asked of a call | 6 on a tool page, 7 on the pillar, 4 on the blog |
| Used, median | 2, in a median 656 words |
| Finished pages that passed the SEO score's keyword mark | 4 of 7 |
| The three that failed | short by 1, 1 and 4 uses |
| Warnings that landed on a page that passed | 9 of the 18 |
| "approval workflows" without "automated" | 55 |
| "automated workflows" | 7 |
| The singular | 3 |
| A changed hyphen or spacing | 0 |

## Cause, from the code

1. **The count was sized to a page the calls did not write.** `SeoKeywordMentionsFor` took 0.6% of
   the page's word floor, 18 for 3,000 words, and gave a call its share by section count: 6 for a
   call sized at 1,000 to 1,200 words. The call wrote about 650.
2. **The same sentence gave a second number that disagreed.** "Roughly once every 200 words" is 3 at
   that length, not 6.
3. **The writer shortens the phrase rather than repeating it.** 65 variations against 74 exact uses.
4. **The check counted against the ask, not against what the page is scored on.** The score passes
   at 0.4% of the page's words. Four pages passed it and were warned anyway.

## What was built

**1. No prompt states a count.** The KEYWORD FREQUENCY paragraph is gone from `SeoBodyInstruction`
(`ContentPromptBuilder.cs`), and `SeoKeywordMentionsFor` with it. The keyword's heading rule (one H2,
on the first batch) and the opening's rule stay. The Workflow product's writer shares the prompt and
loses the sentence too.

**2. `GccKeywordRemap`** (`Services/ContentCreator/Guardrail/`), run on the opening and the body
after the batches and before the linker, the closing and the FAQ, in all three write paths.

- A variation is the keyword with one word left out, never its last: "approval workflows" and
  "automated workflows". A two-word keyword has none. A synonym is not one.
- A middle word always goes back. A first word goes back only where the slot before the phrase is
  open: a sentence start, punctuation, or a function word (a closed list: articles, prepositions,
  conjunctions, pronouns, auxiliaries, a few adverbs). After an adjective or a verb the slot is
  taken and the text stays as written. On the 2026-10-07 pages this reaches 20 of the 54 variations;
  the other 34 sit behind "manual", "customizable", "smart", "automating", "streamline" and the like.
- Case follows the words around the edit. A keyword word with a capital of its own keeps it.
- The page's count is 0.6% of the words it has (`TargetFor`, at least 4), the same share the prompt
  used to state of the floor. It is spread over the opening and the body sections. Only a section
  under its share is touched; the edits are spread over the paragraphs that offer one; one edit per
  paragraph; never in a paragraph that already carries the phrase. Nothing is removed.
- Never touched: headings, quotations, runs with a link or formatting, code, definitions, the
  closing, the FAQ.
- Every edit is on the run log: a `keyword` event with the target, the counts before and after, and
  each edit's heading, from and to.

**3. The keyword is judged once, on the finished page, by the page's own score.** `GccDraftGuard`
reads `GcwSeoAnalyzer.CountKeyword` on the finished page and reports a gap outside the scorer's own
band (0.4% to 2.5%, now named constants the scorer itself reads), naming the sections that never
use the phrase. Never a refusal. The per-call keyword warning in `BatchShortfalls` is gone; the
per-call word floor and the keyword-heading check stay.

**4. The run log shows the numbers as fields.** `batch` carries `keywordUses`; `verdict` carries
`keywordUses` and `keywordDensity`.

**5. Nothing else moves.** No second model call. The scorer's band is unchanged. The document JSON
options are one shared definition (`GccDocumentJson`), used by the service and the guard alike.

## Not in this, stated

- **Revise does not run the remap**, as it does not run the linker. A revised section is the
  writer's text as returned.
- **The FAQ and the closing are not remapped.** The closing is the operator's words; the FAQ is
  written after the closing is attached.
- **The scorer's band is not touched.** Your skills disagree on it: `seo-content` and `seo-page` say
  1 to 3%, `seo-content-brief` says no fixed density. 0.4% to 2.5% stays until you say otherwise.
- **The singular ("automated approval workflow") is not a variation.** Putting an "s" back changes
  the article and the verb around it.
- **Whether the remap and the writer together reach the band** is something only the real run shows.

## Tests

- `GccKeywordRemapTests`: the open and taken slots, the middle word, the exact phrase never extended,
  one edit per paragraph, a section at its share untouched, the spread, list items and subsections,
  headings and quotations and links untouched, two-word keywords and synonyms, "&" and "and", a
  keyword word with its own capital, the target and the shares, no keyword.
- `GccKeywordOnThePageTests`: under, inside and over the band; agreement with
  `GcwSeoAnalyzer.Analyze`; a use in a list item, a quotation or a subsection counted; no keyword.
- `GccBatchShortfallTests` rewritten: words and the heading only, uses counted for the log.
- The prompt tests assert no count sentence and the heading rule.
- Unit suite and integration suite green.

## Verification

Jeff's real run: each `keyword` event's edits and before/after; each `verdict`'s `keywordUses` and
`keywordDensity`; the draft carries one keyword line or none; the SEO report agrees with it. Nothing
is called fixed before that.
