# Email, Social and Ads on Generate, written from the project's Pillar

**Written 2026-10-13. Status: built in both repositories the same day, on Jeff's word ("execute your
plan to turn on PDF, Social and Email"). Not yet seen in a real run. What was built, and where it
differs from what is written below, is under "As built" at the end.**

Jeff, 2026-10-13: *"Add my Email, Social, and Ads, back to generate and make them work from here."*
Asked the same day what each is written from when ticked there, he chose **the project's Pillar**.

Yesterday's change (GeekBackend `93467d0`, content-creator-v2 `a55dc9c`, both pushed 2026-10-13) took
"Email — cold outreach", "Social" and "Ads" off Generate's "Content type" list and put them behind a
"Repurpose" section under "Approve & export" on an approved page. A Generate asked for one of them
is refused today.

## What Generate does once this is built

| On the screen | What happens |
|---|---|
| "Content type" list | "Email — cold outreach", "Social" and "Ads" are on it again, ticked with pages or alone |
| What a piece is written from | The project's Pillar: the one this run just wrote, or the one already saved when "Pillar" is not ticked. It does not have to be approved |
| A project with no Pillar, "Pillar" not ticked | From the Blog, by the same rule |
| No Pillar and no Blog, neither ticked | Refused before anything starts, saying so. Nothing is deleted |
| "Pillar" ticked and refused in the same run | Its pieces are refused too, naming the Pillar. They are not written from the Blog instead. The other pages are kept |
| How many | One of each a project |
| Deleted first | Ticking "Email — cold outreach" deletes every Email of the project before a word is written: the one under the Pillar, any made with Repurpose from another page, and any Generate saved before 2026-10-12. Same for "Social" and "Ads" |
| Order in the run | Pages first, exactly as today. Then the pieces |
| The writer and the check | The ones Repurpose uses: handed the page whole, and from the Brief the reader, the voice and the problem. No search. A figure in a piece must be in the page, or that piece is refused and the rest are kept. No second attempt |
| Where a piece is saved | Under the Pillar. Its tab says "Written from the Pillar", as built |
| Cost | About 3,750 tokens sent for each piece, a few cents for all three (measured 2026-10-12 through the real writer, in made-up text) |
| "Repurpose" under "Approve & export" | Stays as it is: it writes a piece again from one approved page without running Generate. **Say if you want it removed instead** |

A run fails only when nothing at all wrote. A piece that was not written is a line under "Not written",
with why.

## The changes

### GeekBackend

`GeekAPI/Services/ContentCreator/GccGenerationCoordinator.cs`
- `ValidateRequestedTypes` (line 183): the call to `GccRepurpose.GenerateRefusal` goes.
- `RunGenerateAsync` (line 207) splits what was asked into pages and short pieces
  (`GccRepurpose.IsShortType`). A run with no short piece takes today's path, untouched. A run with one:
  1. names the page the pieces are written from, by the rule above, and refuses if there is none;
  2. deletes first: `DeleteOldPagesAsync` for the page types, the new delete below for the pieces;
  3. searches and writes the page types only. No page ticked: no search at all;
  4. saves the pages that wrote (`PersistAllAsync`). It does not fail the run when no page wrote and a
     piece can still be written from a saved Pillar;
  5. reads the source page back (`ListProjectArtifactsAsync`, `ListVersionsAsync`), writes the pieces,
     and saves them under it (`PersistAllAsync(..., parentPageId)`);
  6. gives one result: pages and pieces, every refusal, every warning.
- The writing of one piece inside `RunRepurposeAsync` (lines 403-423) moves to one private method that
  both runs call. `RepurposeEmailAsync`, `RepurposeSocialPostAsync`, `AddImagePromptForContentAsync`
  and `GccRepurpose.FigureRefusal` are used as they are.
- The comment at lines 273-289 is corrected: pages are still written independently of each other; a
  piece waits for its page.

`GeekAPI/Services/ContentCreator/GccRepurpose.cs`
- `GenerateRefusal` is deleted. New: the rule that names the source page for a Generate, with its two
  refusals (the project has no Pillar and no Blog; the page it was to be written from was not written).
- `SourceOf` (line 127) is told whether the page must be approved: yes from the Repurpose route, no
  from Generate.
- `SourceBlock` (line 149): "An approved pillar page" becomes "A pillar page", true for both.

`GeekAPI/Controllers/ContentCreator/GccProjectsController.cs`
- `Generate` (line 359): the no-Pillar-and-no-Blog refusal is answered 400 before a run row exists,
  from the same rule the run uses.

GeekRepository, no migration
- `Repositories/ContentCreator/GccProjectPageRepository.cs`: one new delete, every piece of the given
  types on a project, under a page or under none, with its content.
  Route `DELETE repo/content-creator/projects/{id}/pieces?type=`, and its client method in
  `GeekAPI/HttpClients/HttpGccRepository.cs`.

### content-creator-v2

`src/components/content-creator/ProjectContentWorkspace.tsx`
- `ContentTypePicker` (line 2132) stops leaving the three out. The sentence under the list (2155) says
  what they are written from.
- The empty tab's text (1450) says to tick the type under "Content type" and Generate.
- One line, with Generate held, when a piece is ticked and the project has no Pillar or Blog and
  neither is ticked.

`src/lib/content-creator/repurpose.ts`: `deletedPiecesLine` no longer says "Repurpose wrote"; the
header comment. `src/lib/content-types.ts`: its comments, and its pointer to the PDF plan, which names
a file deleted on 2026-10-10.

## Tests

GeekBackend, `GeekBackend.Tests/ContentCreator`:
- `GccProjectRepurposeRouteTests` line 264: the theory that a Generate is refused naming Repurpose is
  replaced by Generate accepting the short types, and by the 400 when there is nothing to write from.
- New, through the real writers and the real save:
  - Pillar and the three ticked: each piece's request carries the Pillar this run wrote, and each is
    saved under it;
  - "Email — cold outreach" alone on a saved Pillar that is not approved: nothing is searched, the
    Pillar is not deleted, the old Email is deleted first;
  - no Pillar: written from the Blog;
  - Pillar ticked and refused: the pieces are refused and nothing is written from the Blog;
  - Blog refused with a saved Pillar: the Email is still written;
  - a piece refused for a figure keeps the pages and the other pieces;
  - an Email saved before 2026-10-12 is deleted.
- A repository test for the new delete. `GccRepurposeRunTests` stays as it is.

content-creator-v2: `src/repurpose-on-screen.test.tsx` line 261 flips (the list offers the three and
says what they are written from); `src/lib/content-creator/repurpose.test.ts` for the reworded line.

Run: `dotnet build`, both GeekBackend suites (2,139 unit and 44 integration today), and on the form
`npm test`, `tsc --noEmit`, `eslint`.

## The record

`AGENTS.md` § "Repurpose", `docs/technical-specification.md`, `docs/content-creator-sources.md`, and
the line in `plans/use-everything.md` that says a Generate refuses these.

## Commits and push

One commit in GeekBackend, one in content-creator-v2, both on `main`. GeekBackend is pushed first:
until it is live, ticking one of the three is still refused. Before that push I check that no Generate
and no crawl is running, and you hold both until Railway reports SUCCESS on GeekAPI and GeekRepository.

## The proof

Yours, and it costs cents: on a project that already has a Pillar, tick only "Email — cold outreach"
and Generate. Expect one Email under the Pillar, "Written from the Pillar" in its tab, and no line
under "Not written".

## Not in this plan

- Comparison, Alternatives and Listicle. They stay disabled.
- A piece written from a tool page or the Blog by Generate. Generate writes from the Pillar; the
  "Repurpose" section is how a piece is written from another page.
- The six per-channel types (LinkedIn, X, Instagram, Facebook, Meta ads, Google ads). GeekAPI accepts
  them on Generate by the same rule; the form does not offer them.

## As built, 2026-10-13

Everything under "What Generate does once this is built" is how it works. The changes are in the
files the plan names. What differs from the plan, or was not in it:

- **A run that writes nothing says each reason once.** When neither a page nor a piece wrote, the run
  fails with every reason in one message, and no type is announced on its own before that. When
  something wrote, each type that did not is announced as before.
- **Research that cannot be searched for is each page's refusal on such a run, not the run's failure.**
  An email written from the Pillar the project already has needs no research, so it is still written.
  On a run with no short piece a failed search fails the run, as it always did.
- **Pieces that were written and could not be saved, beside pages that were,** are listed under "Not
  written" with why, and the pages stand. With no page saved either, the run fails as any save does.
- **The older route that starts a Generate from a create** (`GccController.Generate`) answers the same
  400 as the project's route.
- **The new delete answers with its own result** (`GccProjectPiecesDeleteResult`): how many pieces of
  the asked types went, and anything saved under one of them.
- **The line under Generate's list says more than the plan asked.** Beside "Writing the Pillar again
  deletes the 3 pieces written from it." it says what a ticked piece replaces: "Writing Email — cold
  outreach again deletes the 2 this project already has." A piece is counted once.
- **The "Repurpose" section's own sentence** now says Generate deletes a piece written there when the
  same piece is ticked under "Content type".
- **The run log** has `short-pieces` (the types, the page they are written from, whether this run
  writes it, how many were deleted) and `piecesWrittenFrom` on `settled`.
- **"Blog" ticked beside a saved Pillar writes the pieces from the saved Pillar,** not from the Blog
  that run writes. That is the rule as written (the Pillar first), stated here because it is the
  case most likely to surprise.

Tests: 2,215 unit and 44 integration in GeekBackend; 84 on the form, with `tsc --noEmit` and `eslint`
clean. The run is tested through the real writers for a Pillar with all three pieces, an email alone
on a saved Pillar that is not approved (nothing searched, the Pillar not deleted, every old email
deleted), a project with only a Blog, a project with neither, a Pillar ticked and refused beside a
Blog, a Pillar refused with nothing else asked for, a Blog refused beside a saved Pillar, pages that
cannot be searched for, and a piece refused for a figure. The repository's new delete is tested
against the real repository.

Not done: no run of it on a real project. The proof above is still Jeff's.
