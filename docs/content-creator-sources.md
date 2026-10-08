# What each source is for: Project site, Partners, Competitors

**Status:** written 2026-10-07 from the code (`GeekAPI/Services/ContentCreator`) and the run log of 2026-10-07. Every
statement below is either checked against one of those or marked **not verified**. Where the operator's own definition
has not been recorded it says so rather than inferring one. This file replaces the claims in
`plans/competitor-partner-data-purpose.md`, which describes the dormant V2 pipeline and the 2026-10-01 state.

**Keep it true:** change this file in the same commit that changes what a source feeds into a prompt.

## At a glance

| | Project site | Partners | Competitors |
|---|---|---|---|
| **The operator enters** | The project's Site URL, which gets a crawl run | The project's Partner URLs | The project's Competitor URLs |
| **Crawl type** | `ProjectSite` | `Partner` | `Competitors` |
| **Retrieved for** | pillar, blog, tool | pillar, blog, tool | pillar, blog, tool |
| **Passages per search** | 8 | 32 per partner site | 8 |
| **The search text** | the keyword alone. From 2026-10-02 to 2026-10-08, 22 fixed words followed it ("the cost, delay and error rate of the manual or status-quo way, the capability that removes it, and measured outcomes"); once the index ran its keyword half they matched every ERP, pricing and integration page, and Jeff had them deleted on 2026-10-08 | the same | "competitor differentiation research; topic: ..." |
| **Must the draft cite it?** | no | tool pages only (a tool page is refused without partner evidence) | no |
| **Pillar call, 2026-10-07** | about 39,400 characters (14%) | 187,700 characters (69%) | 15,200 characters (6%) |

Retrieval happens once per Generate (`GccGroundingResolver`), is held in the running job's memory, and is not stored.
The searches return what matches the keyword best, so **a missing result cannot prove a missing topic**.

## Project site

**Entered:** the project's Site URL. **Original purpose** (`CONTENT_CREATOR_PLAN.md` section 6, "Site Analyzer: gaps +
existing site content into create"): (1) find where the site is missing content, meaning topics and headings with no
page, so the operator can pick a gap and start a create from it; (2) use what the site already has as input to
Generate, so the draft fits the site.

**Today**

1. **Gap finding is gone.** The Site Analyzer that found gaps was backed by the Geek-SEO service, whose persistence
   was deleted on 2026-09-29 as unauthorized (`AGENTS.md`). What is left is `GccV2SiteSection.TryBuildSectionContext`,
   labelled "Legacy Site Analyzer gap picker, v1 only". The operator now enters the keyword. *Not verified: whether the
   frontend still offers a gap picker.*
2. **Three blocks still reach the writer** (sizes from the 2026-10-07 pillar call):
   - **The publisher's own positions** (12,800 characters): the home page's framework, figures and offer, so the page
     is consistent with them.
   - **This site already covers this topic** (4,800): the site structure matched to the keyword. Its subtopics are
     compulsory ("all of which this piece must cover"). A subtopic that lists tools is left out. *Not verified: what it
     listed for the 2026-10-07 run.*
   - **Already published on this site, do not write these again** (21,200): the 8 best-matching passages of your own
     pages. It told the writer not to restate them and never to contradict them.

**Known conflict (Jeff, 2026-10-07).** The "already published" block interferes with rewrites. On 2026-10-07 it listed
the earlier published version of the page being rewritten, so the writer was told not to repeat, and not to contradict,
the page it was replacing. Jeff's position is that this feature was removed; the code still sends it. **Decision
pending:** remove it from long-form prompts after the Pillar run of 2026-10-07 is read. Whether "this site already covers"
has the same effect is open.

## Partners

**Entered:** the project's Partner URLs. **Purpose** (the Create form says "Partners you sell or name"; the operator
confirmed they are third-party tools he sells or promotes, on an affiliate/reseller model): to let the page make
grounded, cited claims about those tools (pricing, integrations, proof points) and not invent or oversell them.

**Today**
- Each declared partner gets its own tool page, written within the keyword and the problem it solves, titled
  "{Tool}: {keyword}".
- A claim about a partner must trace to a retrieved passage and carry that passage's URL. A tool page is refused
  when its partner has too little evidence; a pillar or blog is refused when a declared partner returned no passage.
- A pillar or blog should name every partner (a reported gap when one is missing), and never names a tool the project
  does not list.
- Per partner, pages are extracted into a structured record (features, pricing, FAQs, quotable spans). The extraction is
  stored in the database, keyed by the partner and its pages, and reused.
- In the 2026-10-07 pillar call the partner evidence was 105 passages from 105 pages: Bill 19 (49,000 characters),
  Ramp 23 (43,500), ApprovalMax 20 (38,200), Stampli 23 (32,300), Melio 20 (21,700). The retrieval asks for up to 32
  per site.

## Competitors

**Entered:** the project's Competitor URLs. **The operator's definition has not been recorded.** The 2026-10-01 text
(rival AI-consulting agencies) is out of date; do not rely on it. **Operator to state:** what the Competitors field holds
and what it is for.

**What the code states.** In `GccGroundingResolver`: "competitor evidence is what makes a piece differentiated, which
is the stated purpose of crawling rivals at all."

**What the data was on 2026-10-07.** Pages that rank for the keyword: a whitepaper on DSO optimization (its source is
not shown), "Accounts payable best practices and process guide for 2027" and a NetSuite integrations page, the last two
from an AP-automation vendor, Zone & Co. The prompt itself says they "were retrieved because they rank, not because
they are good".

**Today, two blocks reach the writer**
- **Competitor heading structure** (5,600 characters): "a checklist of what a reader expects covered". A section may
  be tagged `competitor:<exact heading>`, which licenses it as a gap worth covering. Reusing the competitor's heading as
  the section's own is refused.
- **Competitor research** (9,600 characters): "find what they have not said"; never quoted, cited, linked or named.
  Competitor hosts and host-derived names are replaced with "a competitor" and a numbered label
  (`GccCompetitorNames`), but on 2026-10-07 the name "Zone & Co" appeared unredacted inside the passage text. *Not
  investigated.*

## The brief form

Not a crawl source. The brief fields reached the pillar call as about 5,500 characters: the audience, intent, angle, tone,
length and notes. The operator's closing questions are not sent to the writer; the page builds its closing from them
(`GccClosing`). Tools are entered here and in the Partner URLs. The rules we add (SEO scoring 8,500 characters, the
amounts note, the opening already written) are not data.

## Other documents, and how far to trust them

| File | State |
|---|---|
| `CONTENT_CREATOR_PLAN.md` (top of `development/`) | The original product plan. Section 6's gap finder and the site-context gate no longer match the code. |
| `plans/competitor-partner-data-purpose.md` | Stale. Describes the dormant V2 pipeline and the 2026-10-01 state. |
| `plans/fix-the-2026-10-07-generate-run.md` | The 2026-10-07 run: what failed, what changed, what was held. |
| `plans/content-type-agents.md` | The agreed direction: content types as isolated single-responsibility agents. Nothing built yet. |
