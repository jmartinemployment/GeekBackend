# Enable "PDF / LinkedIn document"

**Written 2026-10-13. Status: approved the same day ("execute your plan to turn on PDF, Social and
Email"). Step 1 of six is built and committed on `main`, not pushed; see "As built" at the end. The
type stays "(disabled)" on Generate's "Content type" list until step 5 ships, and until step 4 a
request for it is still refused. The four decisions at the end are settled, as written there.**

Jeff, 2026-10-13: *"Create a plan to enable PDF."*

This replaces the plan of 2026-10-08, which was never approved and was deleted with the old plans on
2026-10-10 (`git show b82cca6:plans/enable-linkedin-document.md` in content-creator-v2). Half of what
that plan pointed at is gone or changed since: the old carousel code, the second attempt after a
failed check, the cuts on research, the Brief's call-to-action fields.

Every `file:line` below was read on 2026-10-13 at GeekBackend `f0d88aa` and content-creator-v2
`a55dc9c`. Read each again at the commit that builds it.

## What it is

A deck of 6 to 12 slides, written as a page of the project, and the LinkedIn post that carries it.
The deck is downloaded as a PDF and posted by hand. One deck a project, named for the keyword, replaced
by each Generate like every other page.

## The rules it is built to

| Rule | Whose word |
|---|---|
| **Not a repurpose.** The deck is written from the Brief and the research, like the Pillar. No page is its source | Jeff, 2026-10-08 |
| **It names every partner**, as the answer to a failure, the way the Pillar does | Jeff, 2026-10-08: *"Yes, it should incorporate"* |
| **Research reaches it the one way every type gets it.** No path of its own | Jeff, 2026-10-12: *"Pillar, Blog, Tools, PDF, etc, all will have one way to receive organizated research, period."* |
| **Nothing entered on the Brief and nothing a search returned is left out for a count** | Jeff, 2026-10-12 |
| **No second attempt.** A deck that fails a check is refused or saved with the gap named | Jeff, 2026-10-06 |
| **The writer returns content, never markup, and writes no link** | `AGENTS.md` § "Body"; Jeff, 2026-10-10 |
| **A Generate is a paid run.** The proof run starts on Jeff's word | Jeff, 2026-10-06 |

## What exists today

- **The two gates.** `linkedindocument` is in GeekAPI's `DisabledContentTypes`
  (`GccGenerateService.cs:240`) and in the form's `DISABLED_CONTENT_TYPES`
  (`src/lib/content-types.ts:77`). A request for it is refused before a run starts
  (`GccGenerationCoordinator.cs:194`).
- **No writer.** `GenerateOneAsync` has a case for the Pillar, the Blog, tool pages and the image
  prompt (`GccGenerationCoordinator.cs:693`). Anything else falls to a generic branch, which is why
  the type is disabled.
- **How a type is added.** A prompt set (`IContentTypePrompts`: `Key`, `OutlineFor`, `Lede`, `Body`)
  registered in `Program.cs:134-139` beside `PillarPrompts`, `BlogPrompts` and `ToolPrompts`. The
  Pillar's writer is the sequence to follow: `Writers/GccGenerateService.Pillar.cs`, 305 lines.
- **No PDF code.** The old deck code was deleted with the second generator on 2026-10-10. Its PDF
  renderer is in git (`git show 73c5cc1:GeekAPI/Services/ContentCreatorV2/Carousel/GccV2LinkedInCarouselPdfService.cs`,
  118 lines). The two libraries are still referenced and unused: QuestPDF 2025.12.3 and PdfPig 0.1.16
  (`GeekAPI.csproj:30-31`).
- **Export already carries a file that is not text.** `ExportedHtmlDocument(FileName, Content, BinaryContent)`,
  written to the zip by `GccExportZip.cs:21` and returned by the one-page export at `GccController.cs:643`.
- **Names left over from the deleted code**, to remove when the gate opens: `GccChannelTypes.LinkedInCarousel`
  and `IsLinkedIn` (read only by `GcwContentTypeScoring.cs:14`), and `linkedin-document` in
  `GccLongFormTypes.cs:40`, the list of types given hero and section image prompts.
- **On the form**, the type's length is the LinkedIn post's (`brief-catalog.ts:943` sends it to
  `socialLinkedIn`), and `src/lib/config.ts:64-76` holds a second list of content types.
- **GeekRepository needs nothing.** A page's body is stored as one JSON string under its type's name.

## The deck

In the code it is the document every page is (`ContentDocument`): the cover is the opening, each slide
is a top-level section whose heading is the slide's title and whose one list holds its lines.

| Slide | What it owes the reader | Written from | Written by |
|---|---|---|---|
| Cover | The failure the deck is about, as the reader lives it. A title and one line | "The core problem" on the Brief; the keyword | The writer, in the same request as the post |
| The problem | What goes wrong today and what it costs | The Brief's failures for every partner; the research | The writer |
| One for each partner | The failure this partner answers and what is done about it. The partner is named in the lines, never in the title | That partner's "Core pain point", its rows and its "The automation to pitch — the marketing/sales hook"; that partner's passages | The writer |
| The approach, end to end | How the pieces fit, in the order they happen | "The automation to pitch"; the publisher's own positions | The writer |
| What to do next | The booking words and the operator's questions | `CompanyProfile:ConsultationClosing*`; the Brief's "One question per line" | Code (`GccClosing`), never the writer |

Numbers: two to eight partners, so six to twelve slides. A title of at most ten words. Two to four
lines a slide, each at most fourteen words. The cover's line at most twenty. The deck 300 to 750 words.

A project with one partner, or more than eight, is refused before a request is paid for, saying how
many it has.

## The post that carries it

LinkedIn shows a document under a post. The post is written in the cover's request, so one request
returns both. It is held to the figure and the currency checks. It is saved in the page's envelope as
`caption`, shown under the slides on the form, and exported beside the PDF as a text file.

## How it is written

One new file, `Writers/GccGenerateService.LinkedInDocument.cs`, following the Pillar's sequence and
using the same pieces:

1. The partners, the publisher's profile and the research, resolved as the Pillar resolves them. A
   partner whose crawl returned no passage refuses the deck, naming the partner, as it refuses a
   Pillar (`PartnerEvidenceRefusal`, `GccGenerationCoordinator.cs:134`).
2. The outline, from the Brief and the partner list (`LinkedInDocumentPrompts.OutlineFor`).
3. The cover and the post, one request. Then every slide in one request: seven slides are about 350
   words, and a request writes about 650 before it stops on its own
   (`MeasuredWordsPerBodyCall`, `GccGenerateService.cs:3237`).
4. **Every request goes through `RequestThatFitsAsync`** (`GccGenerateService.cs:3701`) with the
   page's whole research (`GccPageResearch.PagesOf`), counted before it is sent, and carries the block
   of what the operator entered for each partner (`GccPartnerAnswers.Instruction`). This is the one
   way; the deck gets no shorter research of its own.
5. The last slide appended by code. No link placed: `GccToolLinker` is not run on a deck.
6. Every check once (`GuardedDraftAsync`, `GccGenerateService.cs:3467`), then the envelope
   `{ title, caption, warnings, body }`.

Not done for a deck: the keyword remap and the keyword checks (a deck is not scored for search),
People Also Ask, image prompts, the meta description, JSON-LD, a canonical address.

In `GenerateOneAsync` it is one new case, `linkedindocument`, beside the Pillar's.

## The checks

One function, `GccDraftGuard.LinkedInDocument`, run once on the finished deck.

| Finding | What happens |
|---|---|
| A figure that is not in the evidence | Its sentence deleted and named, as on every page since 2026-10-13 (`GccInventedFigures`) |
| An amount not in US dollars | Refused, as on every page |
| A quotation | Refused, as on the Pillar and the Blog |
| A tool the project does not list is named | Not checked, as on the Pillar and the Blog since 2026-10-13 |
| A link anywhere in the deck | Refused |
| A slide's title names a product | Refused (`tools-section`) |
| "Ask yourself" framing in the writer's own words | Refused (`questions-quiz`) |
| A partner is not named on its slide | Saved, with the gap named (`partner-mentions`) |
| A slide over its numbers: too many lines, a line or a title too long, too few or too many slides | Saved, with the slide named. The PDF still holds one slide a page, because the renderer fits a slide's text to its page |

The post is checked for figures and currency with the deck.

## The PDF

- **One renderer**, `GccLinkedInDocumentPdfRenderer`, makes the PDF from the stored document: one
  slide a page, 1080 by 1350, a page counter, the cover and the last slide on their own ground. It is
  written again from the 118 lines in git, not restored: that code read a slide model of its own, asked
  for Arial by name (the Railway container has none), used colours that are not the site's, and sat
  beside a parser that made up a slide's role when the reply left it out.
- **This is not a second producer of HTML.** `SectionHtmlRenderer` stays the only place tag characters
  are made. The PDF renderer reads the same document and writes no markup.
- **No bytes are stored.** The PDF is made when asked for, from the saved document, as the HTML is.
  Stored bytes could differ from the stored text.
- **Routes.** `GET api/geek-content-creator/versions/{id}/pdf` beside `versions/{id}`
  (`GccController.cs:624`), answering `application/pdf`, and 400 by name for a version that is not a
  deck. The project export zip gains `linkedin-document/{slug}.pdf` and `{slug}.caption.txt`
  (`GccArtifactExportService`, `FolderFor` at line 298).
- **Fonts** are files shipped inside GeekAPI and registered with QuestPDF at startup.
- **Tests read the PDF back** with PdfPig: the page count, the cover's title on page one, no address
  on any page, the booking words on the last.

## What Generate offers once it is open

"PDF / LinkedIn document" can be ticked like "Pillar". Its tab shows the slides as titled lists and the
post under them. "Approve & export" on the deck gains "Download PDF". "Email — cold outreach", "Social"
and "Ads" are not written from a deck: they are written from the Pillar
(`plans/email-social-ads-on-generate.md`).

## What it costs to run

Not measured yet, and it is measured before the first run (step 2). From the Pillar's measured sizes
on research of the 15:29 UTC run of 2026-10-12: the cover's request about 127,000 tokens and the
slides' request about 157,000. A deck is those two requests, some 285,000 tokens sent: about 57 cents
on GPT-4.1 if nothing is billed as a repeat, less when a Pillar or a Blog is written in the same run
and the research is billed as repeated. Nearly all of it is the research, sent whole.

## The steps, one commit each

| # | What | Done when |
|---|---|---|
| 1 | **Built 2026-10-13.** The prompt set: `ContentTypes/LinkedInDocumentPrompts.cs`, its outline, the cover-and-post reply shape, registered in `Program.cs` and in the tests' registry (`TestContentTypePrompts.cs`) | A five-partner Brief gives nine slides in the order above; a Brief with one partner gives the refusal |
| 2 | The writer and the checks: `Writers/GccGenerateService.LinkedInDocument.cs`, `GccDraftGuard.LinkedInDocument`, the case in `GenerateOneAsync`, the deck's numbers in `ContentLengthTargets` | A deck is written through the real writer with a stand-in model; one test for each check; `BriefFieldReachTests` gains the deck and fails if a Brief box reaches none of its requests; each request's size is counted and written in this plan |
| 3 | The renderer, the route, the export | The read-back test; the route answers a PDF for a deck and 400 for a Pillar; the zip of a project with a deck holds the two files |
| 4 | Open GeekAPI's gate: the type leaves `DisabledContentTypes`; the leftover names go; `AGENTS.md`, the specification and the sources document say what was built | Both suites pass. Pushed on Jeff's word, with no Generate and no crawl running; Railway SUCCESS on both services |
| 5 | The form: the type leaves `DISABLED_CONTENT_TYPES`; its own length band; "Download PDF"; the post under the slides; the unused second list of content types deleted if nothing reads it | `npm test`, `tsc`, `eslint`; the screen read in the browser |
| 6 | The proof | Jeff runs one Generate with "PDF / LinkedIn document" alone and reads the PDF: every partner on its slide, no link inside, every figure in the evidence, the booking words on the last page, a post that reads as a LinkedIn post |

The form is pushed after GeekAPI: the other order offers a type the server refuses.

## Not in this plan

- The other disabled types. Each is its own plan.
- Posting to LinkedIn. The PDF and the post are downloaded and posted by hand.
- Images on slides. A slide is text on colour.
- A PDF of a Pillar, a Blog or a tool page.
- A deck cut down from a page that already exists.

## The four decisions, settled 2026-10-13

1. **Links.** None inside the PDF, because LinkedIn does not make a link inside a document clickable.
   The post carries them, added by code: the booking link and each partner's tool page on the site.
   The last slide prints the booking words and the site's address as text. *Taken as written: Jeff
   approved the plan without changing it.*
2. **Hashtags.** None on the post; you add any when posting. *Taken as written.*
3. **The look.** The site's own: navy `#0B162A`, the orange `#C83803` used once a deck, on the booking
   line, and the site's Sora for titles. *Taken as written.*
4. **The QuestPDF licence.** Its free licence is for organisations under one million US dollars a
   year in revenue. *Jeff, 2026-10-13: "Yes, under $1M". The free Community licence is used.*

Say so if any of the first three should be different; each is a small change before step 3 or 4.

## As built

### Step 1, 2026-10-13: what a deck is before it is written

Not pushed. Nothing a run does has changed: the type is still on GeekAPI's disabled list, and the
prompt set is registered ahead of a writer that does not exist yet.

**Files.** `GccLinkedInDocument.cs` (the slide list, the refusal, the two replies and their readers,
the slide's place in the page's document), `ContentTypes/LinkedInDocumentPrompts.cs` (the prompt
set), `Writers/ContentPromptBuilder.LinkedInDocument.cs` (the wording of the two requests), the
deck's numbers in `ContentLengthTargets`, `GccNicheFraming.DeckGuidance`,
`GccPublisherPositions.DeckApproachSlotGuidance`. Tests: `LinkedInDocumentPromptsTests`, 33 of them.
2,248 unit and 44 integration tests pass.

**The slide list is made by code from the project's partners,** never by the writer:
`GccLinkedInDocument.Slides`. Five partners give the cover, the problem, the five in the project's
order, the approach, what to do next. `PartnerRefusal` says why a project with one partner, none or
nine cannot have a deck, with the count and the names.

**Your own skills set what a slide and the post are asked to be.** I had written the wording from
scratch and then found your `social` skill covers LinkedIn document posts
(`references/carousel-frameworks.md`) and `post-writer` covers the post. What they require and where
it now is:

| Your skill says | In the requests |
|---|---|
| Slide 1 is the thumbnail: it has to stop the scroll with nothing behind it | The cover is told exactly that |
| One idea a slide; a deck is not an article cut into squares | Both requests |
| A count promised on the cover is exactly delivered | "it has 5 slides on partner tools ... Say 5 or say no count" |
| Claims and numbers are real, no invented stats | No figure that is not in the evidence, no customer, no case study, on the cover, the slides and the post |
| The post above the document is a second hook, its own post, never "see attached" | The post is told so, and not to repeat the cover's words |
| "Keep posts between 150 and 300 words" (`post-writer`) | The post is asked for 150 to 300 words. This plan had given it no length |
| The final slide has one call to action, not three | The last slide is the booking words and your questions, built by code: one action |
| One visual template for every interior slide; text legible at thumbnail size, about 28pt at 1080 wide at the least | **For step 3, the renderer.** Written here so it is built to them |

The deck's order (a cover, the problem, one slide a partner, the approach, one call to action) is
this plan's and was not changed. It is that reference's numbered-list shape, with its last slide's
two jobs, the summing up and the call to action, on two slides.

**Where step 1 differs from what this plan said:**

1. **A slide's reply is a title and its lines, as strings,** not the section shape a page's body
   returns. Code makes the page's document from them (`CoverSection`, `SlideSection`). The writer has
   no paragraph, no nesting, no quotation and no link it could return. A reply with another number
   of slides than were assigned is refused, never fitted: a slide belongs to the partner its place
   gives it.
2. **The deck's numbers went into `ContentLengthTargets` now,** not in step 2: the requests state
   them.
3. **"The deck 300 to 750 words" is not asked for and will not be checked.** The slide numbers
   cannot reach 300 for a small deck: two partners are four written slides, and with every title and
   line at its longest that is about 330 words with the cover and the last slide, about 200 as a
   writer would write them. The slide numbers are the deck's size; the checks table above never had
   a row for a word count.
4. **The reply reservations are 1,024 tokens for the cover and the post and 2,048 for the slides,**
   sized to what each reply can hold (about 450 and about 1,200). A reservation is what a reply
   that ends in blanks can cost, as two did on 2026-10-13.
5. **Neither request carries the pages' block of tools to name and link.** A deck carries no link,
   and its partners are named by its own slide list.
6. **The subject's keyword is given with a line saying a deck is not written to it.** A deck is not
   scored for search.

**For step 2, found while building this:**

- A count on the cover ("5 tools") must not be read as a figure the evidence does not hold: the
  number of partner slides goes into what the figure check reads for a deck.
- The deck's requests print the brief through the same builder as a page, which also prints the
  Brief's "Length" band when one is set. A deck has no length band until step 5 gives the form one,
  so the writer's context for a deck carries none.
- The two requests without any research are about 3,000 and 3,400 tokens (a five-partner Brief of a
  few lines). The research is the rest, and is measured in step 2 through the real writer.
