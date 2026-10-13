using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator.Guardrail;

// The guard for a "PDF / LinkedIn document" (plans/enable-pdf.md): every check on the finished deck,
// once, by the checks a page is held to wherever a deck has the same thing to check.
public static partial class GccDraftGuard
{
    /// <summary>The check a post longer than LinkedIn takes fails.</summary>
    public const string PostLengthCheck = "post-length";

    /// <summary>
    /// The most characters LinkedIn takes in a post. Jeff's <c>social</c> skill,
    /// <c>references/platform-limits.md</c>: "Max post chars 3,000".
    /// </summary>
    public const int LinkedInPostMaxCharacters = 3_000;

    /// <summary>
    /// Every check a deck is held to, on the deck with its post as the section after the last slide
    /// (<see cref="GccLinkedInDocument.WithPost"/>).
    /// </summary>
    /// <param name="slides">What each slide of the deck is, in order, the cover first.</param>
    /// <remarks>
    /// <para>
    /// <b>Refused:</b> a number the evidence does not hold where it could not be taken out (a slide's
    /// title, the cover's one line, a slide left with nothing but that sentence); money that is not in
    /// US dollars; a quotation; a link or a typed address; the writer framing questions as a quiz on a
    /// slide.
    /// </para>
    /// <para>
    /// <b>Saved, with the gap named:</b> a partner its own slide does not name; a deck with no last
    /// slide.
    /// </para>
    /// <para>
    /// <b>Not checked, and why.</b> A tool the project does not list is named: as on the pillar and the
    /// blog since 2026-10-13. A slide's title names its partner: the request asks for the name in the
    /// lines, and a deck is not refused, or listed, for naming its own partner in a title. A slide
    /// over its numbers (a line a word too long, a fifth line): on the run's record
    /// (<see cref="SlideSizes"/>), not in the operator's list, because the PDF fits a slide's text to
    /// its page and there is nothing to act on. The keyword: a deck is not scored for search.
    /// </para>
    /// <para>
    /// <b>A slide's title is read for figures,</b> where a page's heading is not
    /// (<see cref="GccFigureGrammar"/>: "7 signs your process is broken" is a list title). A slide's
    /// title is a statement in the largest type on the slide, so "Close the books 3 days faster" is a
    /// claim. Code cannot take a title out the way it takes a line out, so one that states a number
    /// the evidence does not hold refuses the deck, as a sentence that is all a section has under it
    /// refuses a page.
    /// </para>
    /// </remarks>
    public static GccGuardVerdict LinkedInDocument(
        ContentDocument document, GccGuardInputs inputs, IReadOnlyList<GccDeckSlidePlan> slides)
    {
        var findings = new List<GccGuardFinding>();
        var deck = GccLinkedInDocument.Label;
        var afterTheCover = slides.Skip(1).ToList();
        var slideSections = document.Sections.Take(afterTheCover.Count).ToList();
        var post = GccLinkedInDocument.PostOf(document, slides.Count);

        // The slides a request wrote: the cover, and every slide after it that code did not build.
        var writtenSlides = new List<Section> { document.Lede };
        for (var i = 0; i < slideSections.Count; i++)
        {
            if (!afterTheCover[i].BuiltByCode) writtenSlides.Add(slideSections[i]);
        }

        // A deck's document holds no quotation by construction: a slide's reply is strings, and code
        // makes the sections. Checked all the same, so the promise does not rest on how it is built.
        var quotes = CountQuotes(document);
        if (quotes > 0)
        {
            findings.Add(new GccGuardFinding(
                "no-quotation",
                $"The {deck} carries {quotes} block quotation(s). A deck does not quote.",
                Refuses: true));
        }

        AddDeckLinkFindings(document, post is null ? writtenSlides : [.. writtenSlides, post], findings);

        // The figure and the currency checks, on the deck with the written slides' titles as lines of
        // their own: what the checks read on a page leaves headings out.
        var withTitles = document with
        {
            Sections =
            [
                .. document.Sections,
                new Section(
                    Tag: "h2",
                    Heading: string.Empty,
                    Paragraphs: [.. writtenSlides
                        .Where(slide => !string.IsNullOrWhiteSpace(slide.Heading))
                        .Select(slide => (Paragraph)new TextParagraph([new Run(slide.Heading)]))],
                    Href: null,
                    Children: []),
            ],
        };
        AddNumberFindings(withTitles, inputs, findings);
        AddCurrencyFindings(withTitles, inputs, findings);

        // The publisher's questions are on the last slide, built by code, and are answered when the
        // reader books. A slide the writer wrote does not turn questions into a quiz. The post is not
        // held to this: it is a post in its own right, and a hook may put a question to its reader.
        var slideText = string.Join(Environment.NewLine, writtenSlides.Select(TextOf));
        var quiz = QuizPhrasings.Where(phrase => slideText.Contains(phrase, StringComparison.OrdinalIgnoreCase)).ToList();
        if (quiz.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "questions-quiz",
                $"The {deck} frames questions as a quiz on a slide (\"{string.Join("\", \"", quiz)}\"). The "
                + "publisher's own questions are on the last slide, and the reader answers them when booking.",
                Refuses: true));
        }

        // Each partner has a slide, and the slide names it: in its lines as the request asks, or in
        // its title. One line for the deck's partners together.
        var unnamed = new List<string>();
        for (var i = 0; i < slideSections.Count; i++)
        {
            if (afterTheCover[i].Kind != GccDeckSlideKind.Partner || afterTheCover[i].Partner is not { Length: > 0 } partner) continue;
            if (!GccRequiredToolMentions.NamesAsWord(TextOf(slideSections[i]), partner))
            {
                // The slide's place in the deck as a reader counts it: the cover is slide 1.
                unnamed.Add($"{partner} (slide {i + 2}, \"{slideSections[i].Heading}\")");
            }
        }

        if (unnamed.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "partner-mentions",
                $"The {deck} does not name {string.Join(", ", unnamed)} on "
                + (unnamed.Count == 1 ? "its slide" : "their slides")
                + ". The deck is saved as written.",
                Refuses: false));
        }

        if (!slides.Any(slide => slide.BuiltByCode))
        {
            findings.Add(new GccGuardFinding(
                "deck-last-slide",
                $"The {deck} has no last slide. It is built from the publisher's booking words, and the "
                + "scheduler's anchor or the closing wording is not configured (CompanyProfile). The deck is "
                + "saved without it.",
                Refuses: false));
        }

        return new GccGuardVerdict(findings);
    }

    /// <summary>
    /// What the operator is told when the post is longer than LinkedIn takes, or null. Read on the
    /// post as it is saved, after the checks.
    /// </summary>
    public static string? PostLength(string? post)
    {
        var characters = (post ?? string.Empty).Length;
        return characters <= LinkedInPostMaxCharacters
            ? null
            : $"The {GccLinkedInDocument.Label}'s post is {characters:N0} characters. LinkedIn takes "
              + $"{LinkedInPostMaxCharacters:N0} in a post: shorten it before posting.";
    }

    /// <summary>
    /// What each slide holds against the deck's numbers, for the run's record: its title's words, how
    /// many lines it has and its longest line's words, with what is over named.
    /// </summary>
    /// <remarks>
    /// Not a finding. The request states the numbers and a writer overshoots them by a word; the PDF
    /// fits a slide's text to its page whatever it holds, so a line here is nothing the operator can
    /// act on. It is kept where a run is read.
    /// </remarks>
    public static IReadOnlyList<object> SlideSizes(ContentDocument deck, IReadOnlyList<GccDeckSlidePlan> slides)
    {
        var sections = new List<Section> { deck.Lede };
        sections.AddRange(deck.Sections.Take(Math.Max(0, slides.Count - 1)));

        var sizes = new List<object>();
        for (var i = 0; i < sections.Count && i < slides.Count; i++)
        {
            var slide = sections[i];
            var isCover = slides[i].Kind == GccDeckSlideKind.Cover;
            // The cover's one line is its paragraph; every other slide's lines are its list's items.
            List<string> lines = isCover
                ? [.. slide.Paragraphs.OfType<TextParagraph>().Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)))]
                : [.. slide.Paragraphs.OfType<ListParagraph>()
                    .SelectMany(list => list.Items)
                    .Select(item => string.Concat(item.Select(run => run.Text)))];
            var titleWords = WordCount(slide.Heading);
            var longestLine = lines.Count == 0 ? 0 : lines.Max(WordCount);
            var lineLimit = isCover
                ? ContentLengthTargets.LinkedInDocumentCoverLineMaxWords
                : ContentLengthTargets.LinkedInDocumentLineMaxWords;

            var over = new List<string>();
            if (!slides[i].BuiltByCode)
            {
                if (titleWords > ContentLengthTargets.LinkedInDocumentTitleMaxWords)
                {
                    over.Add($"a title of {titleWords} words ({ContentLengthTargets.LinkedInDocumentTitleMaxWords} at most)");
                }

                if (!isCover && lines.Count > ContentLengthTargets.LinkedInDocumentMaxLines)
                {
                    over.Add($"{lines.Count} lines ({ContentLengthTargets.LinkedInDocumentMaxLines} at most)");
                }

                if (!isCover && lines.Count < ContentLengthTargets.LinkedInDocumentMinLines)
                {
                    over.Add($"{lines.Count} line(s) ({ContentLengthTargets.LinkedInDocumentMinLines} at least)");
                }

                if (longestLine > lineLimit) over.Add($"a line of {longestLine} words ({lineLimit} at most)");
            }

            sizes.Add(new
            {
                slide = i + 1,
                kind = slides[i].Kind.ToString().ToLowerInvariant(),
                partner = slides[i].Partner,
                title = slide.Heading,
                titleWords,
                lines = lines.Count,
                longestLine,
                outsideItsNumbers = over,
            });
        }

        return sizes;
    }

    /// <summary>
    /// A link set on any part of the deck, or an address typed into what a request wrote. LinkedIn does
    /// not make a link inside a document clickable, and the post's links go in the first comment.
    /// </summary>
    /// <remarks>
    /// An address is one written with its scheme or with "www.". A bare name such as "Bill.com" is not
    /// read as one: a product can be named for its domain, and a slide that names its partner is doing
    /// what it was asked.
    /// </remarks>
    private static void AddDeckLinkFindings(ContentDocument document, IReadOnlyList<Section> written, List<GccGuardFinding> into)
    {
        var found = Hrefs(document).Select(href => href.Trim()).ToList();
        foreach (var section in written)
        {
            found.AddRange(TypedAddress().Matches(TextOf(section)).Select(match => match.Value.TrimEnd('.', ',', ';', ':', ')')));
        }

        if (found.Count == 0) return;

        into.Add(new GccGuardFinding(
            "links",
            $"The {GccLinkedInDocument.Label} carries a link or a typed address: "
            + string.Join(", ", found.Distinct(StringComparer.OrdinalIgnoreCase))
            + ". LinkedIn does not make a link inside a document clickable, so a deck carries none, and the "
            + "booking link and each partner's page are listed for the first comment by code.",
            Refuses: true));
    }

    /// <summary>A section's heading and everything under it as text, a paragraph or a list item to a line.</summary>
    private static string TextOf(Section section) =>
        string.Join(
            Environment.NewLine,
            new[] { section.Heading }
                .Concat(section.Paragraphs.SelectMany(paragraph => paragraph switch
                {
                    ListParagraph list => list.Items.Select(item => string.Concat(item.Select(run => run.Text))),
                    _ => [string.Concat(Runs(paragraph).Select(run => run.Text))],
                }))
                .Where(line => !string.IsNullOrWhiteSpace(line)));

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex TypedAddress();
}
