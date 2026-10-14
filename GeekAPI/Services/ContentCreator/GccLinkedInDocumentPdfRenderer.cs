using System.Globalization;
using System.Text;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// A "PDF / LinkedIn document" as the PDF that is posted: one slide a page, from the saved deck
/// (<c>plans/enable-pdf.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a second producer of HTML.</b> <c>SectionHtmlRenderer</c> stays the only place tag
/// characters are made. This reads the same document and draws text on a page; it writes no markup.
/// </para>
/// <para>
/// <b>No bytes are stored.</b> The PDF is made when it is asked for, from the saved deck, as a page's
/// HTML is. Stored bytes could differ from the stored text.
/// </para>
/// <para>
/// <b>What a slide looks like is set by Jeff's <c>social</c> skill</b> (its carousel reference, which
/// covers LinkedIn document posts): slides are 1080 by 1350; every interior slide has one layout, type
/// scale and palette; text is legible at thumbnail size, about 28pt at 1080 wide at the least. And by
/// the site: its navy, its orange used only on the booking line (as the site sets a button, white
/// words on orange), Sora for a title and Figtree for the lines, as the site sets headings and body
/// text.
/// </para>
/// <para>
/// <b>A slide's text is fitted to its page, and never by guessing.</b> Each slide is set at the
/// largest of a few sizes at which it fits one page, found by laying it out
/// (<see cref="Fit"/>). A slide that does not fit at the smallest is scaled down to fit and
/// reported as set under the legible size, so the operator hears of it when the deck is written.
/// The Brief's questions are not squeezed this way: <see cref="LastSlides"/> carries them on to
/// another slide.
/// </para>
/// <para>
/// <b>The fonts are inside GeekAPI.</b> The Railway container has no font to ask for by name, so no
/// font of the machine is used at all. A character one face does not hold is drawn from the other
/// (Sora has no arrow; Figtree has). A character neither holds would print as an empty box: a hyphen
/// or a space of another kind is drawn as the plain one, a character with no shape of its own is left
/// out, and anything else is reported when the deck is written (<see cref="CharactersNotHeld"/>).
/// </para>
/// </remarks>
public static class GccLinkedInDocumentPdfRenderer
{
    /// <summary>A slide's width, in points: the 1080 of a 1080 by 1350 slide.</summary>
    public const float PageWidth = 1080f;

    /// <summary>A slide's height, in points.</summary>
    public const float PageHeight = 1350f;

    /// <summary>
    /// The smallest size a slide's text is set at. Jeff's <c>social</c> skill: "Text is legible at
    /// thumbnail size (minimum ~28pt at 1080px wide)".
    /// </summary>
    public const float LegibleTextSize = 28f;

    private const float MarginHorizontal = 96f;
    private const float MarginVertical = 88f;

    /// <summary>The site's navy: the ground of the cover and the last slide, and the words on every other.</summary>
    public const string Navy = "#0B162A";

    /// <summary>The site's orange, used only for the band the booking words sit on.</summary>
    public const string Orange = "#C83803";

    private const string White = "#FFFFFF";


    private const string TitleFace = "GccDeckTitle";
    private const string TextFace = "GccDeckText";

    /// <summary>Whether Sora holds a character, by its code point.</summary>
    private static readonly Func<int, bool> TitleFaceHolds;

    /// <summary>Whether Figtree holds a character, by its code point.</summary>
    private static readonly Func<int, bool> TextFaceHolds;

    /// <summary>
    /// The sizes a slide is tried at, largest first: its title's, and its lines'. The last of each is
    /// the smallest a slide is set at without being scaled.
    /// </summary>
    private sealed record Step(float Title, float Text);

    private static readonly Step[] CoverSteps =
    [
        new(112, 48), new(100, 46), new(88, 44), new(76, 40), new(64, 36), new(56, 32), new(48, LegibleTextSize),
    ];

    private static readonly Step[] InteriorSteps =
    [
        new(80, 48), new(72, 46), new(64, 42), new(56, 38), new(50, 34), new(44, 30), new(40, LegibleTextSize),
    ];

    private static readonly Step[] LastSteps =
    [
        new(72, 44), new(64, 42), new(56, 38), new(50, 34), new(44, 30), new(40, LegibleTextSize),
    ];

    static GccLinkedInDocumentPdfRenderer()
    {
        // The Community licence: for organisations under one million US dollars a year in revenue
        // (Jeff, 2026-10-13: "Yes, under $1M").
        QuestPDF.Settings.License = LicenseType.Community;
        // Only the two faces registered below. A font of the machine would differ between a laptop
        // and the container.
        QuestPDF.Settings.UseEnvironmentFonts = false;
        // A character neither face holds is drawn as an empty box and the PDF is still made: a deck
        // is not lost to one character.
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

        TitleFaceHolds = Register(TitleFace, "GeekAPI.Assets.Fonts.Sora-Bold.ttf");
        TextFaceHolds = Register(TextFace, "GeekAPI.Assets.Fonts.Figtree-Regular.ttf");
    }

    /// <summary>Registers one face under <paramref name="face"/> and answers which characters it holds.</summary>
    private static Func<int, bool> Register(string face, string resource)
    {
        using var font = typeof(GccLinkedInDocumentPdfRenderer).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The font {resource} is not inside GeekAPI. A deck's PDF cannot be set without it.");
        using var copy = new MemoryStream();
        font.CopyTo(copy);
        var bytes = copy.ToArray();

        using var registered = new MemoryStream(bytes);
        QuestPDF.Drawing.FontManager.RegisterFontWithCustomName(face, registered);

        // The font's own table of the characters it has a shape for.
        var characters = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable
            ?? throw new InvalidOperationException($"The font {resource} lists no characters. A deck's PDF cannot be set with it.");
        return character => characters.TryGetGlyphIndex(character, out var glyph) && glyph != 0;
    }

    /// <summary>How one slide is set.</summary>
    /// <param name="Slide">Its place in the deck, the cover being 1.</param>
    /// <param name="Title">The slide's title.</param>
    /// <param name="TitleSize">The size its title is set at, in points.</param>
    /// <param name="TextSize">The size its lines are set at.</param>
    /// <param name="UnderLegibleSize">True when the slide did not fit at the smallest size and is
    /// scaled down to fit, which sets its lines under <see cref="LegibleTextSize"/>.</param>
    /// <param name="BuiltByCode">True for a slide of what to do next, which holds the Brief's questions
    /// and is written by no request, so writing the deck again does not change it.</param>
    public sealed record SlideFit(
        int Slide, string Title, float TitleSize, float TextSize, bool UnderLegibleSize, bool BuiltByCode = false);

    /// <summary>
    /// How each slide of a deck is set: the largest sizes at which it fits one page, found by laying
    /// the slide out.
    /// </summary>
    public static IReadOnlyList<SlideFit> Fit(GccLinkedInDocument.SavedDeck deck, CompanyProfileOptions company)
    {
        var slides = SlidesOf(deck);
        var fits = new List<SlideFit>(slides.Count);
        foreach (var slide in slides)
        {
            var steps = StepsFor(slide.Plan.Kind);
            var chosen = steps[^1];
            var fitted = false;
            foreach (var step in steps)
            {
                if (!FitsOnePage(slide, slides.Count, step, company)) continue;
                chosen = step;
                fitted = true;
                break;
            }

            fits.Add(new SlideFit(
                slide.Number, slide.Section.Heading, chosen.Title, chosen.Text,
                UnderLegibleSize: !fitted, BuiltByCode: slide.Plan.BuiltByCode));
        }

        return fits;
    }

    /// <summary>
    /// The slide of what to do next as the slides it takes to be read on a phone: one, when every
    /// question fits it at <see cref="LegibleTextSize"/>; otherwise the questions in the order typed over
    /// the fewest slides that each hold their share at that size, shared as evenly as those slides
    /// allow, every one under the same booking words and the site's address on the last.
    /// </summary>
    /// <param name="next">The slide as <see cref="GccLinkedInDocument.NextSlide"/> built it, every
    /// question on it.</param>
    /// <param name="number">Its place in the deck, the cover being 1: what its page's foot says.</param>
    /// <param name="company">The publisher, whose name is at the foot of the page.</param>
    /// <remarks>
    /// <para>
    /// <b>Why.</b> On 2026-10-14 the first deck of a real run, for "Cash Flow Forecasting: Intelligent
    /// Resource Planning", put the Brief's twelve questions (296 words) on one slide; no
    /// size of the seven fitted it, so it was scaled under 28 points and the operator was told to write
    /// the deck again, which could not change a slide no request writes. What the operator typed is
    /// carried on to another slide, and none of it is cut.
    /// </para>
    /// <para>
    /// <b>Found by laying the slides out,</b> as <see cref="Fit"/> finds a size, never by counting
    /// characters: for each question, how many of the ones after it a slide that starts with it holds
    /// at the smallest size. That gives the fewest slides, and of the ways to put the questions on that
    /// many, the one whose fullest slide has the fewest characters is taken. A question that does not
    /// fit a page alone has a slide to itself and is the one slide set under that size, which
    /// <see cref="QuestionTooLongLine"/> names.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Section> LastSlides(Section next, int number, CompanyProfileOptions company)
    {
        var questions = next.Paragraphs.OfType<ListParagraph>().SelectMany(list => list.Items).ToList();
        var n = questions.Count;
        if (n <= 1) return [next];

        // The site's address, and anything else under the questions, closes the last of the slides.
        var after = next.Paragraphs.Where(paragraph => paragraph is not ListParagraph).ToList();
        Section SlideOf(int first, int last)
        {
            IReadOnlyList<Paragraph> closing = last == n - 1 ? after : [];
            return next with
            {
                Paragraphs = [new ListParagraph(false, [.. questions.Skip(first).Take(last - first + 1)]), .. closing],
            };
        }

        // Questions first to last, inclusive, on one slide at the smallest size. The foot's numbers do
        // not change its height, so the slide is laid out under its first place in the deck.
        var plan = new GccDeckSlidePlan(GccDeckSlideKind.Next);
        bool Fits(int first, int last) =>
            FitsOnePage(new Slide(number, plan, SlideOf(first, last)), number, LastSteps[^1], company);

        if (Fits(0, n - 1)) return [next];

        // reach[first]: the last question a slide that starts at first holds. A slide that holds a run
        // holds any run inside it, so each start begins where the one before it reached.
        var reach = new int[n];
        var reached = 0;
        for (var first = 0; first < n; first++)
        {
            reached = Math.Max(reached, first);
            while (reached + 1 < n && Fits(first, reached + 1)) reached++;
            reach[first] = reached;
        }

        var slides = 0;
        for (var first = 0; first < n; first = reach[first] + 1) slides++;

        return [.. Shared(questions, slides, reach).Select(run => SlideOf(run.First, run.Last))];
    }

    /// <summary>
    /// The questions on <paramref name="slides"/> slides, in order, each slide holding no more than its
    /// <paramref name="reach"/> allows, the fullest slide with as few characters as can be.
    /// </summary>
    private static List<(int First, int Last)> Shared(
        IReadOnlyList<IReadOnlyList<Run>> questions, int slides, IReadOnlyList<int> reach)
    {
        var n = questions.Count;
        // Characters of the first i questions.
        var upTo = new int[n + 1];
        for (var i = 0; i < n; i++) upTo[i + 1] = upTo[i] + Drawn(string.Concat(questions[i].Select(run => run.Text))).Length;

        // fullest[p, i]: the fullest slide when the first i questions are on p slides, or int.MaxValue
        // when no such way fits; from[p, i]: where the last of those slides starts.
        var fullest = new int[slides + 1, n + 1];
        var from = new int[slides + 1, n + 1];
        for (var p = 0; p <= slides; p++)
        {
            for (var i = 0; i <= n; i++) fullest[p, i] = int.MaxValue;
        }

        fullest[0, 0] = 0;
        for (var p = 1; p <= slides; p++)
        {
            for (var i = 1; i <= n; i++)
            {
                for (var first = 0; first < i; first++)
                {
                    if (fullest[p - 1, first] == int.MaxValue || reach[first] < i - 1) continue;
                    var worst = Math.Max(fullest[p - 1, first], upTo[i] - upTo[first]);
                    if (worst >= fullest[p, i]) continue;
                    fullest[p, i] = worst;
                    from[p, i] = first;
                }
            }
        }

        var runs = new List<(int First, int Last)>(slides);
        var end = n;
        for (var p = slides; p >= 1; p--)
        {
            var first = from[p, end];
            runs.Insert(0, (first, end - 1));
            end = first;
        }

        return runs;
    }

    /// <summary>The deck as a PDF, one slide a page.</summary>
    public static byte[] Render(GccLinkedInDocument.SavedDeck deck, CompanyProfileOptions company) =>
        Render(deck, company, Fit(deck, company));

    /// <summary>The deck as a PDF, set as <paramref name="fits"/> says, for a caller that has already fitted it.</summary>
    public static byte[] Render(
        GccLinkedInDocument.SavedDeck deck, CompanyProfileOptions company, IReadOnlyList<SlideFit> fits)
    {
        var slides = SlidesOf(deck);
        return Document
            .Create(document =>
            {
                foreach (var slide in slides)
                {
                    var fit = fits[slide.Number - 1];
                    Page(document, slide, slides.Count, new Step(fit.TitleSize, fit.TextSize), company, scaleToFit: fit.UnderLegibleSize);
                }
            })
            .WithMetadata(new DocumentMetadata { Title = deck.Title, Author = company.PublisherName ?? string.Empty })
            .GeneratePdf();
    }

    /// <summary>The characters of one slide that neither face holds.</summary>
    /// <param name="Slide">The slide's place in the deck, the cover being 1.</param>
    /// <param name="Characters">Each such character once, in the order met.</param>
    public sealed record NotHeld(int Slide, IReadOnlyList<string> Characters);

    /// <summary>
    /// The characters of a deck that would print as an empty box: those neither face holds and
    /// <see cref="Drawn"/> has nothing to draw in the place of. One entry for each slide that has any.
    /// </summary>
    public static IReadOnlyList<NotHeld> CharactersNotHeld(GccLinkedInDocument.SavedDeck deck)
    {
        var found = new List<NotHeld>();
        foreach (var slide in SlidesOf(deck))
        {
            var characters = new List<string>();
            foreach (var text in LinesOf(slide.Section).Concat(ParagraphsOf(slide.Section)).Prepend(Drawn(slide.Section.Heading)))
            {
                foreach (var rune in text.EnumerateRunes())
                {
                    if (Holds(rune) || rune.Value is '\n' or '\r') continue;
                    var character = rune.ToString();
                    if (!characters.Contains(character)) characters.Add(character);
                }
            }

            if (characters.Count > 0) found.Add(new NotHeld(slide.Number, characters));
        }

        return found;
    }

    /// <summary>
    /// The line the operator reads when slides a request wrote had to be set under
    /// <see cref="LegibleTextSize"/> to fit, or null when none was. A slide of what to do next is
    /// <see cref="QuestionTooLongLine"/>'s: writing the deck again does not change it.
    /// </summary>
    public static string? SetSmallLine(IReadOnlyList<SlideFit> fits)
    {
        var small = fits.Where(fit => fit.UnderLegibleSize && !fit.BuiltByCode).ToList();
        if (small.Count == 0) return null;

        var named = string.Join("; ", small.Select(fit => $"slide {fit.Slide} (\"{fit.Title}\")"));
        return small.Count == 1
            ? $"In the PDF, {named} holds more than fits a page at a size that reads on a phone, so its text is set "
              + $"smaller than {LegibleTextSize:0} points. Write the deck again for a shorter slide."
            : $"In the PDF, {small.Count} slides hold more than fits a page at a size that reads on a phone, so their "
              + $"text is set smaller than {LegibleTextSize:0} points: {named}. Write the deck again for shorter slides.";
    }

    /// <summary>
    /// The line the operator reads when a slide of what to do next is set under
    /// <see cref="LegibleTextSize"/>, or null when none is. <see cref="LastSlides"/> carries the
    /// questions on to as many slides as they need, so this is a single question longer than a whole
    /// slide holds.
    /// </summary>
    public static string? QuestionTooLongLine(IReadOnlyList<SlideFit> fits)
    {
        var small = fits.Where(fit => fit.UnderLegibleSize && fit.BuiltByCode).ToList();
        if (small.Count == 0) return null;

        var named = small.Count == 1
            ? $"slide {small[0].Slide} is"
            : $"slides {string.Join(", ", small.SkipLast(1).Select(fit => fit.Slide))} and {small[^1].Slide} are";
        return $"In the PDF, a question from the Brief's \"One question per line\" is longer than one slide holds at a "
            + $"size that reads on a phone, so {named} set smaller than {LegibleTextSize:0} points. A shorter "
            + "question there is set larger; nothing else on the deck changes.";
    }

    /// <summary>
    /// The line the operator reads when the deck uses characters the PDF cannot draw, or null when it
    /// uses none.
    /// </summary>
    public static string? EmptyBoxLine(IReadOnlyList<NotHeld> notHeld)
    {
        if (notHeld.Count == 0) return null;

        var named = string.Join("; ", notHeld.Select(slide => $"slide {slide.Slide}: {string.Join(" ", slide.Characters)}"));
        return "The PDF's two typefaces have no shape for some characters this deck uses, and each prints as an "
            + $"empty box ({named}). Write the deck again to be rid of them.";
    }

    private static bool Holds(Rune character) => TitleFaceHolds(character.Value) || TextFaceHolds(character.Value);

    /// <summary>
    /// A slide's text as it is drawn. A character either face holds is drawn as written. Of the rest:
    /// a hyphen of another kind is drawn as the plain hyphen and a space of another kind as the plain
    /// space, being the same mark to a reader; a character with no shape of its own (a joiner, a
    /// variation selector) is left out; anything else is kept, prints as an empty box, and is what
    /// <see cref="CharactersNotHeld"/> reports.
    /// </summary>
    internal static string Drawn(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // An accented letter written as a letter and a separate accent becomes the one character the
        // faces hold.
        var composed = text.Normalize(NormalizationForm.FormC);
        var drawn = new StringBuilder(composed.Length);
        foreach (var rune in composed.EnumerateRunes())
        {
            if (Holds(rune) || rune.Value is '\n' or '\r')
            {
                drawn.Append(rune.ToString());
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                // U+2010 hyphen, U+2011 non-breaking hyphen: the models write the second inside compound words.
                case UnicodeCategory.DashPunctuation when rune.Value is 0x2010 or 0x2011:
                    drawn.Append('-');
                    break;
                case UnicodeCategory.SpaceSeparator:
                    drawn.Append(' ');
                    break;
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark when rune.Value is >= 0xFE00 and <= 0xFE0F:
                    break;
                default:
                    drawn.Append(rune.ToString());
                    break;
            }
        }

        return drawn.ToString();
    }

    /// <summary>One slide with what it is and where it stands.</summary>
    private sealed record Slide(int Number, GccDeckSlidePlan Plan, Section Section);

    private static List<Slide> SlidesOf(GccLinkedInDocument.SavedDeck deck)
    {
        var slides = new List<Slide> { new(1, deck.Slides[0], deck.Deck.Lede) };
        for (var i = 0; i < deck.Deck.Sections.Count; i++)
        {
            slides.Add(new Slide(i + 2, deck.Slides[i + 1], deck.Deck.Sections[i]));
        }

        return slides;
    }

    private static Step[] StepsFor(GccDeckSlideKind kind) => kind switch
    {
        GccDeckSlideKind.Cover => CoverSteps,
        GccDeckSlideKind.Next => LastSteps,
        _ => InteriorSteps,
    };

    /// <summary>
    /// Whether a slide set at <paramref name="step"/> takes one page: it is laid out alone, and a
    /// slide that does not fit runs on to a second page.
    /// </summary>
    private static bool FitsOnePage(Slide slide, int of, Step step, CompanyProfileOptions company)
    {
        try
        {
            var alone = Document
                .Create(document => Page(document, slide, of, step, company, scaleToFit: false))
                .GeneratePdf();
            using var read = UglyToad.PdfPig.PdfDocument.Open(alone);
            return read.NumberOfPages == 1;
        }
        catch (QuestPDF.Drawing.Exceptions.DocumentLayoutException)
        {
            // Something on the slide cannot be placed at this size at all: it does not fit.
            return false;
        }
    }

    private static void Page(
        IDocumentContainer document, Slide slide, int of, Step step, CompanyProfileOptions company, bool scaleToFit)
    {
        var onNavy = slide.Plan.Kind is GccDeckSlideKind.Cover or GccDeckSlideKind.Next;
        var ink = onNavy ? White : Navy;
        document.Page(page =>
        {
            page.Size(PageWidth, PageHeight);
            page.MarginHorizontal(MarginHorizontal);
            page.MarginVertical(MarginVertical);
            page.PageColor(onNavy ? Navy : White);
            page.DefaultTextStyle(style => style.FontFamily(TextFace, TitleFace).FontSize(step.Text).FontColor(ink).LineHeight(1.25f));

            var content = page.Content().PaddingBottom(32).AlignMiddle();
            if (scaleToFit) content = content.ScaleToFit();
            switch (slide.Plan.Kind)
            {
                case GccDeckSlideKind.Cover:
                    Cover(content, slide.Section, step);
                    break;
                case GccDeckSlideKind.Next:
                    Last(content, slide.Section, step);
                    break;
                default:
                    Interior(content, slide.Section, step);
                    break;
            }

            // The publisher on the left and the slide's place in the deck on the right, on every slide.
            page.Footer().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text(Drawn(company.PublisherName))
                    .FontFamily(TitleFace, TextFace).FontSize(LegibleTextSize).FontColor(ink);
                row.ConstantItem(200).AlignRight().Text($"{slide.Number} / {of}")
                    .FontFamily(TextFace, TitleFace).FontSize(LegibleTextSize).FontColor(ink);
            });
        });
    }

    /// <summary>The cover: its title, and its one line under it.</summary>
    private static void Cover(IContainer content, Section cover, Step step) =>
        content.Column(column =>
        {
            column.Spacing(48);
            column.Item().Text(Drawn(cover.Heading)).FontFamily(TitleFace, TextFace).FontSize(step.Title).LineHeight(1.08f);
            foreach (var line in ParagraphsOf(cover))
            {
                column.Item().Text(line);
            }
        });

    /// <summary>
    /// Every slide between the cover and the last, on the one layout: the title, a short rule, and the
    /// slide's lines, each with a square marker in front.
    /// </summary>
    private static void Interior(IContainer content, Section slide, Step step) =>
        content.Column(column =>
        {
            column.Spacing(40);
            column.Item().Text(Drawn(slide.Heading)).FontFamily(TitleFace, TextFace).FontSize(step.Title).LineHeight(1.1f);
            column.Item().Width(120).Height(8).Background(Navy);
            foreach (var line in LinesOf(slide))
            {
                column.Item().Element(item => Line(item, line, step, Navy));
            }

            foreach (var paragraph in ParagraphsOf(slide))
            {
                column.Item().Text(paragraph);
            }
        });

    /// <summary>
    /// A slide of what to do next: the booking words in white on the orange band, the operator's
    /// questions under it, and on the last of these slides the site's address as words.
    /// </summary>
    private static void Last(IContainer content, Section slide, Step step) =>
        content.Column(column =>
        {
            column.Spacing(40);
            column.Item().Background(Orange).PaddingVertical(36).PaddingHorizontal(44)
                .Text(Drawn(slide.Heading)).FontFamily(TitleFace, TextFace).FontSize(step.Title).FontColor(White).LineHeight(1.1f);
            foreach (var line in LinesOf(slide))
            {
                column.Item().Element(item => Line(item, line, step, White));
            }

            foreach (var paragraph in ParagraphsOf(slide))
            {
                column.Item().PaddingTop(16).Text(paragraph).FontFamily(TitleFace, TextFace);
            }
        });

    /// <summary>One line of a slide: a square marker drawn as a shape, and the line's words beside it.</summary>
    private static void Line(IContainer item, string line, Step step, string color) =>
        item.Row(row =>
        {
            var marker = step.Text * 0.34f;
            row.ConstantItem(marker + 28).PaddingTop(step.Text * 0.48f).Width(marker).Height(marker).Background(color);
            row.RelativeItem().Text(line);
        });

    /// <summary>A slide's lines as they are drawn: the items of its lists, in order.</summary>
    private static IEnumerable<string> LinesOf(Section slide) =>
        slide.Paragraphs.OfType<ListParagraph>()
            .SelectMany(list => list.Items)
            .Select(item => Drawn(string.Concat(item.Select(run => run.Text))).Trim())
            .Where(line => line.Length > 0);

    /// <summary>What a slide holds besides its lines, as drawn: the cover's one line, the site's address on the last slide.</summary>
    private static IEnumerable<string> ParagraphsOf(Section slide) =>
        slide.Paragraphs.OfType<TextParagraph>()
            .Select(paragraph => Drawn(string.Concat(paragraph.Runs.Select(run => run.Text))).Trim())
            .Where(paragraph => paragraph.Length > 0);
}
