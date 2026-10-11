# The 18:26 run of 2026-10-10: pillar and blog not written

Written 2026-10-10. **Status: approved by Jeff on 2026-10-10. Both changes are built, one commit
each on `main` (`b135a67`, `fa71599`); 1,868 unit and 44 integration tests pass. Not yet seen in a
real run: the sizes under "What the log measures" are worked out from the 18:26 log, not read from
a call that ran with these changes.** The findings are read from the run's own log (109 events, job
`5ef1165c`) and compared with the 12:14 run of the same day (132 events, job `d0438c61`). Where
something is not a reading of the log, it says so.

## What the run did

Project "Cash Flow Forecasting: Automated Accounts Receivable", started 18:26:16 UTC, ended
18:34:52. Five tool pages written. Pillar and blog refused, each by OpenAI with 400:

> This model's maximum context length is 128000 tokens. However, you requested 141637 tokens
> (125253 in the messages, 16384 in the completion).

Seven gap lines on the tool pages, against 29 on the 12:14 run.

## Cause of the two refusals

A change of that afternoon, `3dc6eb8`: a later search keeps its passages on a page an earlier
search already returned. Until then the later answer was dropped whole.

| | 12:14 run | 18:26 run |
|---|---|---|
| Partner evidence in a pillar body call | 385,864 characters | 577,347 characters |
| The whole pillar body prompt | 442,822 characters, 89,405 tokens | 634,180 characters, 125,253 tokens |
| With 16,384 reserved for the reply | 105,789 of 128,000 | 141,637 of 128,000, refused |
| The blog body prompt | 446,962 characters, 90,249 tokens | 637,662 characters, 125,975 tokens |

Everything else in the prompt is the same size on both runs to within a few hundred characters.
The tool pages' body calls grew too (Upflow 50,000 to 73,000 tokens, Versapay 35,000 to 54,000)
and still fit.

**Why nothing caught it.** The merge was tested for merging correctly. The prompt it produces was
never measured, though the 12:14 run's log had the sizes. No code knows the model's limit, and the
pillar was already at 83% of it.

**The opening calls were paid for.** The pillar's and the blog's opening call each went through at
about 120,000 prompt tokens (their reply is reserved 2,048); the first body call was the one
refused.

## Jeff's decisions, 2026-10-10

- **Keep the extra passages and make room** ("Keep it, make room"), not undo the change.
- **Leave the keyword as it is.**

## What the log measures for the fix

In the pillar body prompt, 326 passages are printed with a `Context:` line and a
`Specific detail:` line. In 205 of them the two lines are the same text: a section shorter than the
Library's child window gives a child passage identical to its parent, and both are printed.

| | Pillar body call | Blog body call |
|---|---|---|
| Characters saved by printing those 205 once | 89,323 | 89,323 |
| Prompt after | about 107,600 tokens | about 108,300 tokens |
| With 16,384 reserved, as now | about 124,000 of 128,000 | about 124,700 of 128,000 |
| With 8,192 reserved | about 115,800 of 128,000 | about 116,500 of 128,000 |

The largest reply of any two-section call in the run was 1,146 tokens, for 777 words. 8,192 is
seven times that. The 16,384 was set on 2026-09-28, when one call wrote a whole blog body and
6,144 cut it short (`ContentPromptBuilder.cs:247-265`); a call writes two sections now.

Printing once alone would fit by 3%. Lowering the reservation alone would not fit at all
(125,253 + 8,192). Both together leave about 9%.

## Change 1: a passage whose two copies are the same text is printed once

`GeekAPI/Services/GeekCrawler/HttpGeekCrawlerRagClient.cs`, `RenderChunk` (about line 1080). When a
child passage's parent text and its own text are the same after trimming and the existing
truncation, the text is printed once with no labels, as a passage with no parent is printed today.
A child that differs from its parent still prints both.

- Nothing reads those labels back. They are written in `RenderChunk` and parsed nowhere.
- The prompt already tells the writer "Not every passage carries every label"
  (`GccGenerateService.cs:320`). No prompt text changes.
- Every block built from search results shrinks: pillar, blog, tool body, tool FAQ.
- Tests, in `GeekBackend.Tests/GeekCrawler/HttpGeekCrawlerRagClientTests.cs`: an identical parent
  and child print once with no label; a differing pair prints both.

## Change 2: a call that writes two sections reserves 8,192 for its reply

`GeekAPI/Services/Workflow/Services/PromptBuilders/ContentPromptBuilder.cs`. One helper beside
`LongFormBodyMaxOutputTokens`, `BodyMaxOutputTokens`: a call that writes a part of the page, two
sections at most, reserves 8,192; a call that writes the whole outline, or more than two sections,
keeps 16,384. Read by the three builders that write in batches:

- `BuildArticleSectionBatchPrompt` (`Writers/ContentPromptBuilder.Pillar.cs:404`)
- `BuildStandaloneBlogBodyPrompt` (`ContentPromptBuilder.cs:2399`)
- `BuildToolBodyPrompt` (`ContentPromptBuilder.cs:2826`)

`BuildBlogBodyPrompt` (`:2186`), the Workflow product's whole-body call, is not touched, and the
Workflow product's whole-outline tool and blog calls keep 16,384.

**Built differently from how this was first written,** which said "a body call of at most two
sections". An existing test (`The_output_budget_can_hold_the_word_floor_it_asks_for`) holds a call
that writes the whole page to its floor at 5.4 tokens a word, and a whole page of one or two
sections is still a whole page. So the rule reads the page's section count as well as the call's.

- 8,192 holds two sections at the most they are asked for: 2 x 700 words x 5.4 is 7,560.
- Tests, in `OutlinePromptRulesTests.cs`: a two-section pillar, blog and tool call of a longer page
  reserves 8,192, and that holds two sections at their longest; a three-section call keeps 16,384;
  the Workflow product's tool call, handed the whole outline, keeps 16,384.

## What this leaves

- **Margin: about 9%.** A project whose partners have larger pages can overflow again. When it
  does, OpenAI refuses the call and the page is not written, after its opening call has been paid
  for. Nothing in code prevents that, and this plan adds no size budget.
- **Cost.** Each pillar and blog call carries about 20% more input than at 12:14.

## The pasted review, checked

| It says | Checked |
|---|---|
| The reply size of 16,384 is the issue; lower it to 4,096 or 8,192 | Half right. 125,253 + 4,096 is still over 128,000, so it would not have fixed this run alone. It is half of change 2 |
| Trim the reference material; do not feed whole pages | The growth is one change of that afternoon, measured above. The evidence is search results, six passages a page at most, not whole pages. The duplicate printing is real and is change 1 |
| Generate section by section | Already so. The pillar is five calls of two sections. Each call carries the whole evidence, which is why the size of the evidence decides it |
| Tell the writer to use the phrase at least once in each named section | A count in the prompt, ruled out on 2026-10-10, and the headings it names are written by the writer during the run. Jeff: leave the keyword as it is |

## The seven lines, and what the next run should list

None of the seven is removed by this plan.

**Keyword, 2 lines (Chaserhq 0.21%, Invoiced 0.33%).** The writer wrote the exact phrase 4 and 3
times. Code put it back in every place grammar allowed, 5 and 9, and stopped there: 9 of 20 and 12
of 19. Chaserhq's page says "accounts receivable" 27 times in all, most after a verb or an
adjective ("managing accounts receivable", "current accounts receivable"), where "automated"
cannot go in front; Invoiced's says it 16 times. Upflow, Versapay and Bill passed at 0.41%, 0.40%
and 0.43%. Left as it is on Jeff's word, so a keyword line on some tool pages is to be expected.

**FAQ, 4 lines.**
- Chaserhq, "Can I add manual entries or apply overrides to Chaser's cash flow forecast?": the
  question reworded that day from the page's own words. The search returned one section each of
  `/integrations/sage200`, `/chaser-api` and `/integrations/sap`, all headed "See how you can
  automate receivables and forecast cash flow". The section of `/cash-flow-forecast` that answers
  it was not returned. The rewording did not work. Why the search prefers those sections is not
  measured: nothing in GeekAPI runs a search without a model call.
- Chaserhq, "How does the Customer Billing Portal interface with cash forecasting?" and "What
  happens to the cash flow forecast if an invoice remains entirely uncollected?"; Versapay, "How
  do customer risk segments influence the cash forecast?". Each was shown the same three passages
  as at 12:14, was answered then and left out now. So a question whose passages do not clearly
  answer it is answered on one run and left out on the next.
- Bill's five questions were all answered: the three removals and two rewordings held.

**Bill, no quotation, 1 line.** The 40 sentences now come one from each page, as changed that
day. A page's first sentence is mostly a description of a customer ("Vermont Flannel is a US-based
clothing manufacturer of flannel clothing"). One of the 40 states a capability ("BILL makes it
simple to create, send, and manage invoices, so you can get paid faster"), and the writer did not
pick it.

**What the next run should list.** Pillar and blog written; their own gap lines are not known,
since neither has been written since 12:14. On the tool pages, between four and eight lines: the
reworded Chaserhq question, probably Bill's quotation, a keyword line on one or more pages, and
whichever of the three unsteady FAQ questions is left out that time.

## Order

1. Change 1, then change 2: done, one commit each on `main`, `dotnet test` on both projects after
   each (1,859 unit and 44 integration before them, 1,868 and 44 after).
2. The record: done. `plans/fix-the-2026-10-10-generate-run.md` and
   `docs/technical-specification.md` say a passage prints once and what a batched call reserves.
   One commit.
3. Push to `main`, only after GeekRepository's log shows no Generate running. Both Railway
   services redeploy; no Generate until both report success.
4. A real run.

## Verification

1. The tests named under each change; both suites pass.
2. After the push, both deployments reach SUCCESS at the pushed commit.
3. The real run, read from its log: the pillar's and the blog's body calls report about 107,000 to
   109,000 prompt tokens and none answers 400; no two-section call's reply ends on `length`; the
   gap list is read against the section above.
