using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Auth;
using GeekAPI.Controllers.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// "PDF / LinkedIn document": the PDF that is posted, its download and its place in a project's
/// export (<c>plans/enable-pdf.md</c>, step 3).
/// </summary>
/// <remarks>
/// The PDF is read back, not trusted: each test opens the bytes with PdfPig and reads the pages. A
/// deck is written through its real writer with a stand-in model
/// (<see cref="BriefFieldReachTests.WriteDeckAsync"/>) and read as it was saved, so what the renderer
/// is handed is what a run would hand it.
/// </remarks>
public sealed class LinkedInDocumentPdfTests
{
    private const string Brief = BriefFieldReachTests.CompleteBrief;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly CompanyProfileOptions Company = new();
    private static readonly Guid ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A line no slide could hold: about 170 words.</summary>
    private static readonly string FarTooLong = string.Join(
        " ", Enumerable.Repeat("The team keys the same balances into a second sheet before anyone reviews them.", 12));

    private static async Task<(string Json, GccLinkedInDocument.SavedDeck Deck)> WrittenDeckAsync(
        Func<string, string, string>? reply = null,
        IReadOnlyList<string>? partnerUrls = null)
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(Brief, reply: reply, partnerUrls: partnerUrls);
        var saved = calls[^1].Text;
        return (saved, GccLinkedInDocument.ReadSaved(saved, Web)
            ?? throw new InvalidOperationException("The deck the writer saved does not read back as a deck."));
    }

    /// <summary>The reply to the slides request with one thing in it changed.</summary>
    private static Func<string, string, string> ChangingSlides(Action<JsonArray> change) =>
        (label, scripted) =>
        {
            if (label != "slides") return scripted;
            var reply = (JsonObject)JsonNode.Parse(scripted)!;
            change((JsonArray)reply["slides"]!);
            return reply.ToJsonString();
        };

    private static List<string> Warnings(string savedJson) =>
        [.. JsonDocument.Parse(savedJson).RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!)];

    /// <summary>Text with no white space: a PDF holds its letters and not the spaces between its words.</summary>
    private static string Squashed(string? text) => new((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Each page of a PDF: its size, and its text with no white space.</summary>
    private static List<(double Width, double Height, string Text)> PagesOf(byte[] pdf)
    {
        using var read = PdfDocument.Open(pdf);
        return [.. read.GetPages().Select(page => (page.Width, page.Height, Squashed(page.Text)))];
    }

    private static string TextOf(Section slide) =>
        string.Join(" ", slide.Paragraphs.SelectMany(paragraph => paragraph switch
        {
            ListParagraph list => list.Items.Select(item => string.Concat(item.Select(run => run.Text))),
            TextParagraph text => [string.Concat(text.Runs.Select(run => run.Text))],
            _ => [],
        }));

    // ---- the PDF, read back ----------------------------------------------------------------------

    [Fact]
    public async Task A_deck_is_one_slide_a_page_at_1080_by_1350_and_every_word_of_it_reads_back()
    {
        var (_, deck) = await WrittenDeckAsync();

        var pdf = GccLinkedInDocumentPdfRenderer.Render(deck, Company);

        var pages = PagesOf(pdf);
        // Two partners: the cover, the problem, a slide each, the approach, what to do next.
        Assert.Equal(6, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.Equal(GccLinkedInDocumentPdfRenderer.PageWidth, page.Width, precision: 1);
            Assert.Equal(GccLinkedInDocumentPdfRenderer.PageHeight, page.Height, precision: 1);
        });

        // The cover's title and its one line are page one.
        Assert.Contains(Squashed("The close that runs on one spreadsheet"), pages[0].Text, StringComparison.Ordinal);
        Assert.Contains(Squashed(TextOf(deck.Deck.Lede)), pages[0].Text, StringComparison.Ordinal);

        // Every other slide is the page in its place, with its title and every one of its lines.
        for (var i = 0; i < deck.Deck.Sections.Count; i++)
        {
            var slide = deck.Deck.Sections[i];
            Assert.Contains(Squashed(slide.Heading), pages[i + 1].Text, StringComparison.Ordinal);
            Assert.Contains(Squashed(TextOf(slide)), pages[i + 1].Text, StringComparison.Ordinal);
        }

        // The last page: the booking words, the Brief's question, the site as words.
        Assert.Contains(Squashed("Answer these questions when booking your free consultation"), pages[^1].Text, StringComparison.Ordinal);
        Assert.Contains(Squashed("How many accounts are reconciled by hand each month?"), pages[^1].Text, StringComparison.Ordinal);
        Assert.Contains("geekatyourspot.com", pages[^1].Text, StringComparison.Ordinal);

        // The publisher and the slide's place in the deck are on every page.
        for (var i = 0; i < pages.Count; i++)
        {
            Assert.Contains(Squashed(Company.PublisherName), pages[i].Text, StringComparison.Ordinal);
            Assert.EndsWith($"{i + 1}/{pages.Count}", pages[i].Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task No_address_is_on_any_page_but_the_sites_on_the_last_and_nothing_in_the_pdf_is_a_link()
    {
        var (_, deck) = await WrittenDeckAsync();

        var pdf = GccLinkedInDocumentPdfRenderer.Render(deck, Company);

        var pages = PagesOf(pdf);
        Assert.All(pages, page =>
        {
            Assert.DoesNotContain("http", page.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("www.", page.Text, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(pages.Take(pages.Count - 1), page =>
            Assert.DoesNotContain("geekatyourspot.com", page.Text, StringComparison.OrdinalIgnoreCase));

        // LinkedIn makes no link inside a document clickable, and none is made here to look as if it were.
        using var read = PdfDocument.Open(pdf);
        Assert.All(read.GetPages(), page => Assert.Empty(page.GetAnnotations()));
    }

    [Fact]
    public async Task The_post_and_the_first_comment_are_not_in_the_pdf_and_its_title_is_the_decks()
    {
        var (_, deck) = await WrittenDeckAsync();
        Assert.False(string.IsNullOrWhiteSpace(deck.Post));
        Assert.Contains("https://", deck.FirstComment, StringComparison.Ordinal);

        var pdf = GccLinkedInDocumentPdfRenderer.Render(deck, Company);

        var everyPage = string.Concat(PagesOf(pdf).Select(page => page.Text));
        Assert.DoesNotContain(Squashed(deck.Post.Split('\n')[0]), everyPage, StringComparison.Ordinal);
        Assert.DoesNotContain(Squashed("Book your free consultation:"), everyPage, StringComparison.Ordinal);

        using var read = PdfDocument.Open(pdf);
        Assert.Equal("The close that runs on one spreadsheet", read.Information.Title);
    }

    [Fact]
    public async Task A_deck_of_eight_partners_is_twelve_pages()
    {
        IReadOnlyList<string> eight = [.. Enumerable.Range(1, 8).Select(n => $"https://partner{n}.test/")];
        var (_, deck) = await WrittenDeckAsync(partnerUrls: eight);

        Assert.Equal(12, PagesOf(GccLinkedInDocumentPdfRenderer.Render(deck, Company)).Count);
    }

    // ---- how a slide is set ----------------------------------------------------------------------

    [Fact]
    public async Task Every_slide_of_an_ordinary_deck_is_set_at_a_legible_size_and_the_operator_is_told_nothing()
    {
        var (saved, deck) = await WrittenDeckAsync();

        var fits = GccLinkedInDocumentPdfRenderer.Fit(deck, Company);

        Assert.Equal(Enumerable.Range(1, 6), fits.Select(fit => fit.Slide));
        Assert.All(fits, fit =>
        {
            Assert.False(fit.UnderLegibleSize);
            Assert.True(fit.TextSize >= GccLinkedInDocumentPdfRenderer.LegibleTextSize);
            Assert.True(fit.TitleSize > fit.TextSize);
        });
        Assert.Null(GccLinkedInDocumentPdfRenderer.SetSmallLine(fits));
        Assert.Empty(GccLinkedInDocumentPdfRenderer.CharactersNotHeld(deck));
        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public async Task A_slide_too_full_for_its_page_is_named_when_the_deck_is_written_and_is_still_one_page()
    {
        var (saved, deck) = await WrittenDeckAsync(ChangingSlides(slides =>
        {
            // The first written slide, the deck's second: four lines no slide could hold.
            var lines = (JsonArray)slides[0]!["lines"]!;
            lines.Clear();
            for (var i = 0; i < 4; i++) lines.Add(FarTooLong);
        }));

        var fits = GccLinkedInDocumentPdfRenderer.Fit(deck, Company);

        Assert.Equal([2], fits.Where(fit => fit.UnderLegibleSize).Select(fit => fit.Slide));

        // The operator hears of it with the deck, in one line that names the slide.
        var line = Assert.Single(Warnings(saved), warning => warning.StartsWith("In the PDF,", StringComparison.Ordinal));
        Assert.Contains($"slide 2 (\"{deck.Deck.Sections[0].Heading}\")", line, StringComparison.Ordinal);
        Assert.Contains("smaller than 28 points", line, StringComparison.Ordinal);
        Assert.Equal(line, GccLinkedInDocumentPdfRenderer.SetSmallLine(fits));

        // The slide is scaled to its one page and not run on to a second, and none of it is cut.
        var pages = PagesOf(GccLinkedInDocumentPdfRenderer.Render(deck, Company));
        Assert.Equal(6, pages.Count);
        Assert.Contains(Squashed(TextOf(deck.Deck.Sections[0])), pages[1].Text, StringComparison.Ordinal);
        Assert.EndsWith("2/6", pages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slide_that_is_merely_long_is_set_smaller_and_still_at_a_legible_size()
    {
        var (saved, deck) = await WrittenDeckAsync(ChangingSlides(slides =>
        {
            var lines = (JsonArray)slides[0]!["lines"]!;
            lines.Clear();
            for (var i = 0; i < 5; i++)
            {
                lines.Add("The team keys the same balances into a second sheet before anyone on the close reviews a single one of them.");
            }
        }));

        var fits = GccLinkedInDocumentPdfRenderer.Fit(deck, Company);

        Assert.False(fits[1].UnderLegibleSize);
        Assert.True(fits[1].TextSize < fits[2].TextSize, "the fuller slide is not set smaller than its neighbour");
        Assert.True(fits[1].TextSize >= GccLinkedInDocumentPdfRenderer.LegibleTextSize);
        Assert.DoesNotContain(Warnings(saved), warning => warning.StartsWith("In the PDF,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_slide_at_the_most_it_is_asked_for_is_set_well_over_the_legible_size()
    {
        // The most a slide is asked for: a title of ten words and four lines of fourteen.
        const string title = "Where the first week of every month goes for controllers";
        const string line = "The team keys the same balances into a second sheet before anyone reviews one.";
        Assert.Equal(ContentLengthTargets.LinkedInDocumentTitleMaxWords, title.Split(' ').Length);
        Assert.Equal(ContentLengthTargets.LinkedInDocumentLineMaxWords, line.Split(' ').Length);

        var (saved, deck) = await WrittenDeckAsync(ChangingSlides(slides =>
        {
            foreach (var slide in slides)
            {
                slide!["title"] = title;
                var lines = (JsonArray)slide["lines"]!;
                // The partner's name stays on its slide: the first line is the slide's own.
                while (lines.Count > 1) lines.RemoveAt(lines.Count - 1);
                while (lines.Count < ContentLengthTargets.LinkedInDocumentMaxLines) lines.Add(line);
            }
        }));

        var fits = GccLinkedInDocumentPdfRenderer.Fit(deck, Company);

        Assert.All(fits.Skip(1).Take(4), fit =>
        {
            Assert.False(fit.UnderLegibleSize);
            Assert.True(fit.TextSize >= 46, $"slide {fit.Slide} is set at {fit.TitleSize} and {fit.TextSize}");
        });
        Assert.DoesNotContain(Warnings(saved), warning => warning.StartsWith("In the PDF,", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_slides_set_small_are_one_line_that_names_both()
    {
        var line = GccLinkedInDocumentPdfRenderer.SetSmallLine(
        [
            new GccLinkedInDocumentPdfRenderer.SlideFit(1, "The cover", 112, 48, UnderLegibleSize: false),
            new GccLinkedInDocumentPdfRenderer.SlideFit(2, "The problem", 40, 28, UnderLegibleSize: true),
            new GccLinkedInDocumentPdfRenderer.SlideFit(3, "A partner", 40, 28, UnderLegibleSize: true),
        ]);

        Assert.StartsWith("In the PDF, 2 slides hold more than fits a page", line, StringComparison.Ordinal);
        Assert.Contains("slide 2 (\"The problem\"); slide 3 (\"A partner\")", line, StringComparison.Ordinal);
        Assert.DoesNotContain("slide 1", line, StringComparison.Ordinal);
    }

    // ---- characters the typefaces do not hold -----------------------------------------------------

    [Theory]
    // The non-breaking hyphen the models write inside compound words, and the hyphen proper.
    [InlineData("month‑end close", "month-end close")]
    [InlineData("month‐end close", "month-end close")]
    // A joiner and a variation selector have no shape of their own.
    [InlineData("zero​width", "zerowidth")]
    [InlineData("a️b", "ab")]
    // A letter and a separate accent are the one character the faces hold.
    [InlineData("café", "café")]
    // What the faces hold is drawn as written: curly quotes, the en dash of a range, an arrow, a sum.
    [InlineData("“Close” in 5–7 days → $1,200 ≥ 10%", "“Close” in 5–7 days → $1,200 ≥ 10%")]
    // A character neither holds is kept, and reported.
    [InlineData("Done ✓", "Done ✓")]
    public void A_slides_text_is_drawn_as_written_but_for_a_hyphen_or_a_space_of_another_kind(string written, string drawn) =>
        Assert.Equal(drawn, GccLinkedInDocumentPdfRenderer.Drawn(written));

    [Fact]
    public async Task A_character_neither_typeface_holds_is_named_with_its_slide_when_the_deck_is_written()
    {
        var (saved, deck) = await WrittenDeckAsync(ChangingSlides(slides =>
        {
            ((JsonArray)slides[0]!["lines"]!).Add("The month‑end close is done ✓");
            ((JsonArray)slides[1]!["lines"]!).Add("Signed off ✓ and filed \U0001F4C1");
        }));

        var notHeld = GccLinkedInDocumentPdfRenderer.CharactersNotHeld(deck);

        // The hyphen is drawn as a plain one and is nothing to report; the check mark and the folder are.
        Assert.Equal([2, 3], notHeld.Select(slide => slide.Slide));
        Assert.Equal(["✓"], notHeld[0].Characters);
        Assert.Equal(["✓", "\U0001F4C1"], notHeld[1].Characters);

        var line = Assert.Single(Warnings(saved), warning => warning.Contains("empty box", StringComparison.Ordinal));
        Assert.Equal(line, GccLinkedInDocumentPdfRenderer.EmptyBoxLine(notHeld));
        Assert.Contains("slide 2: ✓", line, StringComparison.Ordinal);
        Assert.Contains("slide 3: ✓ \U0001F4C1", line, StringComparison.Ordinal);

        // The PDF is still made, with the hyphen as a plain one.
        var pages = PagesOf(GccLinkedInDocumentPdfRenderer.Render(deck, Company));
        Assert.Equal(6, pages.Count);
        Assert.Contains(Squashed("The month-end close is done"), pages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_only_one_typeface_holds_is_drawn_from_the_other()
    {
        // Sora, the titles' face, has no arrow; Figtree, the lines' face, has.
        var deck = new GccLinkedInDocument.SavedDeck(
            "Close → report",
            new ContentDocument(
                new Section("h2", "Close → report", [new TextParagraph([new Run("One line under it.")])], null, []),
                [new Section("h2", "Entry → review", [new ListParagraph(false, [[new Run("A line.")]])], null, [])]),
            [new GccDeckSlidePlan(GccDeckSlideKind.Cover, null), new GccDeckSlidePlan(GccDeckSlideKind.Problem, null)],
            "A post.",
            string.Empty);

        Assert.Empty(GccLinkedInDocumentPdfRenderer.CharactersNotHeld(deck));

        using var read = PdfDocument.Open(GccLinkedInDocumentPdfRenderer.Render(deck, Company));
        foreach (var page in read.GetPages())
        {
            var arrow = Assert.Single(page.Letters, letter => letter.Value == "→");
            var titleLetter = page.Letters.First(letter => letter.Value == "C" || letter.Value == "E");
            Assert.NotEqual(titleLetter.FontName, arrow.FontName);
        }
    }

    // ---- what is read as a deck -------------------------------------------------------------------

    [Fact]
    public async Task A_saved_deck_reads_back_as_its_slides_its_post_and_its_first_comment()
    {
        var (saved, deck) = await WrittenDeckAsync();
        var root = JsonDocument.Parse(saved).RootElement;

        Assert.Equal(root.GetProperty("title").GetString(), deck.Title);
        Assert.Equal(root.GetProperty("caption").GetString(), deck.Post);
        Assert.Equal(root.GetProperty("firstComment").GetString(), deck.FirstComment);
        Assert.Equal(
            [GccDeckSlideKind.Cover, GccDeckSlideKind.Problem, GccDeckSlideKind.Partner, GccDeckSlideKind.Partner,
             GccDeckSlideKind.Approach, GccDeckSlideKind.Next],
            deck.Slides.Select(slide => slide.Kind));
        Assert.Equal(["Partner", "Second"], deck.Slides.Where(slide => slide.Kind == GccDeckSlideKind.Partner).Select(slide => slide.Partner));
        Assert.Equal(5, deck.Deck.Sections.Count);
    }

    [Fact]
    public async Task What_is_not_saved_as_a_deck_is_not_read_as_one()
    {
        var (saved, _) = await WrittenDeckAsync();

        Assert.Null(GccLinkedInDocument.ReadSaved(null, Web));
        Assert.Null(GccLinkedInDocument.ReadSaved("  ", Web));
        Assert.Null(GccLinkedInDocument.ReadSaved("not a page at all", Web));
        Assert.Null(GccLinkedInDocument.ReadSaved("[1, 2]", Web));

        // A pillar: a title and a body, and no list of what its sections are. Its sections are not
        // read as slides by where they stand.
        var pillar = (JsonObject)JsonNode.Parse(saved)!;
        pillar.Remove("slides");
        Assert.Null(GccLinkedInDocument.ReadSaved(pillar.ToJsonString(), Web));

        // A list that does not agree with the document: one slide short, a first slide that is not the
        // cover, a kind that is none of a deck's.
        var short1 = (JsonObject)JsonNode.Parse(saved)!;
        ((JsonArray)short1["slides"]!).RemoveAt(5);
        Assert.Null(GccLinkedInDocument.ReadSaved(short1.ToJsonString(), Web));

        var noCover = (JsonObject)JsonNode.Parse(saved)!;
        ((JsonArray)noCover["slides"]!)[0]!["kind"] = "problem";
        Assert.Null(GccLinkedInDocument.ReadSaved(noCover.ToJsonString(), Web));

        var unknown = (JsonObject)JsonNode.Parse(saved)!;
        ((JsonArray)unknown["slides"]!)[2]!["kind"] = "testimonial";
        Assert.Null(GccLinkedInDocument.ReadSaved(unknown.ToJsonString(), Web));
    }

    // ---- the download ------------------------------------------------------------------------------

    private static GccArtifactDto Page(string type, string name = "Accounts Payable") =>
        new(Guid.NewGuid(), Guid.NewGuid(), ProjectId, null, type, name, "draft", DateTime.UtcNow, DateTime.UtcNow);

    private static (GccArtifactExportService Export, HttpGccRepository Repo) ExportOver(params (GccArtifactDto Page, Guid VersionId, string Body)[] pages)
    {
        var repo = new HttpGccRepository(
            new HttpClient(new Repository(pages)) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        return (new GccArtifactExportService(repo, Options.Create(Company), NullLogger<GccArtifactExportService>.Instance), repo);
    }

    private static GccController ControllerOver(GccArtifactExportService export, HttpGccRepository repo) =>
        new(repo, null!, null!, Options.Create(Company), null!, null!, null!, null!, export, null!, null!, null!, null!, null!, null!,
            NullLogger<GccController>.Instance);

    [Fact]
    public void The_pdf_is_a_route_of_its_own_beside_the_version_and_is_under_the_manage_policy()
    {
        var route = typeof(GccController).GetMethod(nameof(GccController.GetVersionPdf))!;

        Assert.Equal("api/geek-content-creator", typeof(GccController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("versions/{id:guid}/pdf", route.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(ContentCreatorAuthConstants.ManagePolicy, route.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Theory]
    [InlineData("linkedin-document")]
    [InlineData("linkedinDocument")]
    public async Task A_decks_version_answers_its_pdf(string savedType)
    {
        var (saved, _) = await WrittenDeckAsync();
        var versionId = Guid.NewGuid();
        var (export, repo) = ExportOver((Page(savedType), versionId, saved));

        var answer = await ControllerOver(export, repo).GetVersionPdf(versionId, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(answer);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("the-close-that-runs-on-one-spreadsheet.pdf", file.FileDownloadName);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(file.FileContents, 0, 5));
        Assert.Equal(6, PagesOf(file.FileContents).Count);
    }

    [Fact]
    public async Task A_version_that_is_not_a_deck_is_answered_400_saying_so_and_no_version_404()
    {
        var (saved, _) = await WrittenDeckAsync();
        var pillarVersion = Guid.NewGuid();
        var brokenVersion = Guid.NewGuid();
        var (export, repo) = ExportOver(
            // A pillar, even one whose body would read as a deck: the page's type decides.
            (Page("pillar"), pillarVersion, saved),
            (Page("linkedin-document"), brokenVersion, "{\"title\":\"A deck\",\"body\":{}}"));
        var controller = ControllerOver(export, repo);

        var pillar = Assert.IsType<BadRequestObjectResult>(await controller.GetVersionPdf(pillarVersion, CancellationToken.None));
        Assert.Equal(
            "Only a PDF / LinkedIn document has a PDF, and this page is not one: it is saved as \"pillar\".",
            pillar.Value);

        var broken = Assert.IsType<BadRequestObjectResult>(await controller.GetVersionPdf(brokenVersion, CancellationToken.None));
        Assert.Equal(
            "This PDF / LinkedIn document is not saved as a deck of slides, so no PDF can be made from it. Write it again with Generate.",
            broken.Value);

        Assert.IsType<NotFoundResult>(await controller.GetVersionPdf(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_deck_is_not_scored_for_search_and_the_scores_route_says_so()
    {
        var (saved, _) = await WrittenDeckAsync();
        var versionId = Guid.NewGuid();
        var (export, repo) = ExportOver((Page("linkedin-document"), versionId, saved));

        var answer = await ControllerOver(export, repo).Seo(versionId, "AI implementation", CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(answer);
        Assert.Equal(
            "A PDF / LinkedIn document is not scored for search: it is slides, not a page of the site.",
            refused.Value);
    }

    // ---- the export --------------------------------------------------------------------------------

    [Fact]
    public async Task A_projects_export_holds_a_decks_pdf_its_post_and_its_first_comment_and_no_html_for_it()
    {
        var (saved, deck) = await WrittenDeckAsync();
        var pillarBody = JsonSerializer.Serialize(new
        {
            title = "Accounts Payable",
            metaDescription = "A meta description.",
            body = new ContentDocument(
                new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
                [new Section("h2", "How it routes", [new TextParagraph([new Run("Approvals route by amount.")])], null, [])]),
        }, Web);
        var (export, _) = ExportOver(
            (Page("pillar"), Guid.NewGuid(), pillarBody),
            (Page("linkedin-document"), Guid.NewGuid(), saved));

        var documents = await export.ExportProjectAsync(ProjectId, CancellationToken.None);

        Assert.Equal(
            [
                "linkedin-document/the-close-that-runs-on-one-spreadsheet.caption.txt",
                "linkedin-document/the-close-that-runs-on-one-spreadsheet.first-comment.txt",
                "linkedin-document/the-close-that-runs-on-one-spreadsheet.pdf",
                "use-cases/accounts-payable.html",
            ],
            documents.Select(document => document.FileName).Order(StringComparer.Ordinal));

        // The archive itself: the PDF's bytes whole, the post and the comment as their text.
        using var zip = new ZipArchive(new MemoryStream(await GccExportZip.WriteAsync(documents, CancellationToken.None)));
        byte[] Bytes(string name)
        {
            using var entry = zip.GetEntry(name)!.Open();
            using var copy = new MemoryStream();
            entry.CopyTo(copy);
            return copy.ToArray();
        }

        Assert.Equal(6, PagesOf(Bytes("linkedin-document/the-close-that-runs-on-one-spreadsheet.pdf")).Count);
        Assert.Equal(deck.Post, Encoding.UTF8.GetString(Bytes("linkedin-document/the-close-that-runs-on-one-spreadsheet.caption.txt")));
        Assert.Equal(
            deck.FirstComment,
            Encoding.UTF8.GetString(Bytes("linkedin-document/the-close-that-runs-on-one-spreadsheet.first-comment.txt")));
    }

    [Fact]
    public async Task A_decks_one_file_is_its_pdf_and_the_one_page_route_answers_it_as_a_pdf()
    {
        var (saved, _) = await WrittenDeckAsync();
        var page = Page("linkedin-document");
        var (export, repo) = ExportOver((page, Guid.NewGuid(), saved));

        var answer = await ControllerOver(export, repo).ExportArtifactHtml(page.Id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(answer);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("the-close-that-runs-on-one-spreadsheet.pdf", file.FileDownloadName);
        Assert.Equal(6, PagesOf(file.FileContents).Count);
    }

    [Fact]
    public async Task A_deck_that_is_not_saved_as_one_is_exported_as_it_is_saved_and_not_dropped()
    {
        const string body = "{\"title\":\"A deck\",\"body\":{}}";
        var (export, _) = ExportOver((Page("linkedin-document", "The Deck"), Guid.NewGuid(), body));

        var document = Assert.Single(await export.ExportProjectAsync(ProjectId, CancellationToken.None));

        Assert.Equal("linkedin-document/the-deck.json", document.FileName);
        Assert.Equal(body, document.Content);
    }

    /// <summary>GeekRepository, holding the pages it is given: each with its one version.</summary>
    private sealed class Repository((GccArtifactDto Page, Guid VersionId, string Body)[] pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            GccArtifactVersionDto Version((GccArtifactDto Page, Guid VersionId, string Body) held) =>
                new(held.VersionId, held.Page.Id, 1, held.Body, null, 0, DateTime.UtcNow);

            object? result = null;
            if (path == $"/repo/content-creator/projects/{ProjectId}/artifacts")
            {
                result = pages.Select(held => held.Page).ToArray();
            }
            else if (path.StartsWith("/repo/content-creator/creates/", StringComparison.Ordinal))
            {
                result = new GccCreateDto(
                    Guid.Parse(path["/repo/content-creator/creates/".Length..]), Guid.NewGuid(), Guid.NewGuid(), "pillar",
                    "Accounts Payable: Automated Approval Workflows", null, null, null, null, null, "draft",
                    DateTime.UtcNow, DateTime.UtcNow, "accounting", ProjectId);
            }
            else if (path.StartsWith("/repo/content-creator/versions?artifactId=", StringComparison.Ordinal))
            {
                var pageId = Guid.Parse(path["/repo/content-creator/versions?artifactId=".Length..]);
                result = pages.Where(held => held.Page.Id == pageId).Select(Version).ToArray();
            }
            else if (path.StartsWith("/repo/content-creator/versions/", StringComparison.Ordinal))
            {
                var versionId = Guid.Parse(path["/repo/content-creator/versions/".Length..]);
                result = pages.Where(held => held.VersionId == versionId).Select(Version).Cast<object>().FirstOrDefault();
            }
            else if (path.StartsWith("/repo/content-creator/artifacts/", StringComparison.Ordinal))
            {
                var pageId = Guid.Parse(path["/repo/content-creator/artifacts/".Length..]);
                result = pages.Where(held => held.Page.Id == pageId).Select(held => held.Page).Cast<object>().FirstOrDefault();
            }

            return Task.FromResult(result is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(result, Web), Encoding.UTF8, "application/json"),
                });
        }
    }
}
