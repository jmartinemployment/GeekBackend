using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every figure in the opening and the body is cited to the research page it is on: the source's name
/// linked to the page, and the date the page was crawled, placed by code after the sentence (Jeff's
/// ai-seo, seo-content, geo-citability and seo-geo skills; geekatyourspot's GEO audit of 2026-10-20,
/// H3). The writer still writes no link.
/// </summary>
public sealed class GccFigureSourcesTests
{
    private const string ZampUrl = "https://zamp.com/customers";
    private const string QuadernoUrl = "https://quaderno.io/pricing";
    private const string OwnUrl = "https://geek.test/use-cases/sales-tax";
    private const string RivalUrl = "https://rival.test/compare";

    private static readonly DateTimeOffset ZampCrawled = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset QuadernoCrawled = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

    private static GccQuoteablePage Page(string url, string title, DateTimeOffset? crawled, params string[] passages) =>
        new(url, title, [new HeadingDto(2, title)], passages, CrawledAtUtc: crawled);

    private static GccFigureSources.Inputs Inputs(
        IReadOnlyList<GccQuoteablePage>? partners = null,
        IReadOnlyList<GccQuoteablePage>? site = null,
        IReadOnlyList<GccQuoteablePage>? competitors = null,
        string? operatorsWords = null) =>
        new(
            partners ??
            [
                Page(ZampUrl, "Zamp customers", ZampCrawled, "Zamp reports 200K+ hours saved and 100K+ filings on time."),
                Page(QuadernoUrl, "Quaderno pricing", QuadernoCrawled, "Quaderno starts at $49 a month and supports 12 currencies."),
            ],
            site ?? [],
            competitors ?? [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["zamp.com"] = "Zamp", ["quaderno.io"] = "Quaderno" },
            "Geek @ Your Spot",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "geek.test" },
            operatorsWords);

    private static Section Section(string heading, params Paragraph[] paragraphs) => new("h2", heading, paragraphs, null, []);

    private static TextParagraph Text(params Run[] runs) => new(runs);

    private static TextParagraph Text(string text) => new([new Run(text)]);

    private static Section Opening(string text) => Section("Opening", Text(text));

    private static string TextOf(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => string.Concat(text.Runs.Select(run => run.Text)),
        ListParagraph list => string.Join(" | ", list.Items.Select(item => string.Concat(item.Select(run => run.Text)))),
        _ => string.Empty,
    };

    private static IReadOnlyList<Run> Runs(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => text.Runs,
        ListParagraph list => [.. list.Items.SelectMany(item => item)],
        _ => [],
    };

    [Fact]
    public void A_sentence_with_a_figure_is_cited_to_the_page_that_states_it_before_its_full_stop()
    {
        var body = Section("What it saves", Text("Teams have saved 200,000 hours with the tool. The close still has to be run."));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        var paragraph = result.Sections[0].Paragraphs[0];
        Assert.Equal(
            "Teams have saved 200,000 hours with the tool (Zamp, as of Oct 13, 2026). The close still has to be run.",
            TextOf(paragraph));
        var link = Assert.Single(Runs(paragraph), run => run.Href is not null);
        Assert.Equal("Zamp", link.Text);
        Assert.Equal(ZampUrl, link.Href);

        var placed = Assert.Single(result.Placed);
        Assert.Equal("Teams have saved 200,000 hours with the tool.", placed.Sentence);
        Assert.Equal(["200,000 hours"], placed.Figures);
        Assert.Equal(ZampUrl, placed.Url);
        Assert.Equal(ZampCrawled, placed.AsOf);
        Assert.Equal("What it saves", placed.Heading);
        Assert.Equal(0, placed.AlsoOn);
        Assert.Empty(result.Uncited);
    }

    [Fact]
    public void The_citation_adds_no_figure_the_numbers_check_would_have_to_license()
    {
        var body = Section("Pricing", Text("Plans start at $49 a month."));
        var evidence = "Quaderno starts at $49 a month and supports 12 currencies.";

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        var text = TextOf(result.Sections[0].Paragraphs[0]);
        Assert.Equal("Plans start at $49 a month (Quaderno, as of Oct 11, 2026).", text);
        var runs = result.Sections[0].Paragraphs[0] is TextParagraph t ? t.Runs : [];
        var links = new List<(int, int)>();
        var at = 0;
        foreach (var run in runs)
        {
            if (run.Href is not null) links.Add((at, at + run.Text.Length));
            at += run.Text.Length;
        }

        var known = GccFigureGrammar.NumbersIn(evidence);
        var unlicensed = GccFigureGrammar.Find(text, links).Where(figure => !known.Contains(figure.Value)).ToList();
        Assert.Empty(unlicensed);
    }

    [Fact]
    public void A_page_without_a_crawl_date_is_cited_by_name_alone()
    {
        var inputs = Inputs(partners: [Page(ZampUrl, "Zamp customers", null, "Zamp reports 200K+ hours saved.")]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Saved", Text("It saved 200,000 hours."))], inputs);

        Assert.Equal("It saved 200,000 hours (Zamp).", TextOf(result.Sections[0].Paragraphs[0]));
        Assert.Null(Assert.Single(result.Placed).AsOf);
    }

    [Fact]
    public void A_page_is_cited_once_in_a_paragraph_however_many_sentences_take_figures_from_it()
    {
        var body = Section("Scale", Text("Zamp reports 200,000 hours saved. It also counts 100,000 filings on time. Both are its own figures."));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        Assert.Equal(
            "Zamp reports 200,000 hours saved (Zamp, as of Oct 13, 2026). It also counts 100,000 filings on time. Both are its own figures.",
            TextOf(result.Sections[0].Paragraphs[0]));
        Assert.Single(result.Placed);
    }

    [Fact]
    public void A_list_is_one_paragraph_and_each_page_is_cited_on_the_first_item_that_needs_it()
    {
        var list = new ListParagraph(false,
        [
            [new Run("200,000 hours saved across customers.")],
            [new Run("Plans from $49 a month.")],
            [new Run("100,000 filings on time.")],
        ]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("At a glance", list)], Inputs());

        Assert.Equal(
            "200,000 hours saved across customers (Zamp, as of Oct 13, 2026). | Plans from $49 a month (Quaderno, as of Oct 11, 2026). | 100,000 filings on time.",
            TextOf(result.Sections[0].Paragraphs[0]));
        Assert.Equal(2, result.Placed.Count);
    }

    [Fact]
    public void The_partner_a_sentence_names_is_the_one_cited_when_several_pages_state_the_figure()
    {
        var inputs = Inputs(partners:
        [
            Page(ZampUrl, "Zamp", ZampCrawled, "Texas sets its threshold at $500,000."),
            Page(QuadernoUrl, "Quaderno", QuadernoCrawled, "Texas requires a permit once sales pass $500,000."),
        ]);

        var result = GccFigureSources.Cite(
            Opening("An opening."), [Section("Thresholds", Text("Quaderno notes that Texas starts at $500,000."))], inputs);

        var placed = Assert.Single(result.Placed);
        Assert.Equal(QuadernoUrl, placed.Url);
        Assert.Equal("Quaderno", placed.Name);
        Assert.Equal(0, placed.AlsoOn);
        Assert.Equal("Quaderno notes that Texas starts at $500,000 (Quaderno, as of Oct 11, 2026).", TextOf(result.Sections[0].Paragraphs[0]));
    }

    [Fact]
    public void A_sentence_that_names_no_partner_is_cited_to_the_first_page_that_states_it_and_the_others_are_counted()
    {
        var inputs = Inputs(partners:
        [
            Page(ZampUrl, "Zamp", ZampCrawled, "Texas sets its threshold at $500,000."),
            Page(QuadernoUrl, "Quaderno", QuadernoCrawled, "Texas requires a permit once sales pass $500,000."),
        ]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Thresholds", Text("Texas starts at $500,000."))], inputs);

        var placed = Assert.Single(result.Placed);
        Assert.Equal(ZampUrl, placed.Url);
        Assert.Equal(1, placed.AlsoOn);
    }

    [Fact]
    public void A_figure_attributed_to_a_partner_whose_pages_do_not_state_it_is_left_uncited_and_named()
    {
        var body = Section("Pricing", Text("Zamp starts at $49 a month."));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        Assert.Equal("Zamp starts at $49 a month.", TextOf(result.Sections[0].Paragraphs[0]));
        Assert.Empty(result.Placed);
        var uncited = Assert.Single(result.Uncited);
        Assert.Equal(["$49"], uncited.Figures);
        Assert.Equal("the sentence names Zamp, whose pages do not state it; it is on Quaderno's page", uncited.Why);
    }

    [Fact]
    public void A_figure_only_the_operator_typed_is_left_uncited_as_the_operators_own()
    {
        var inputs = Inputs(operatorsWords: "Our clients close in under 60 seconds.");

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Speed", Text("A close takes under 60 seconds."))], inputs);

        Assert.Empty(result.Placed);
        Assert.Equal(GccFigureSources.FromTheBrief, Assert.Single(result.Uncited).Why);
    }

    [Fact]
    public void A_figure_only_a_competitor_states_is_never_cited()
    {
        var inputs = Inputs(competitors: [Page(RivalUrl, "Rival", null, "We serve 3,000 customers.")]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Scale", Text("Some vendors serve 3,000 customers."))], inputs);

        Assert.Empty(result.Placed);
        Assert.Equal(GccFigureSources.OnACompetitorsPage, Assert.Single(result.Uncited).Why);
        Assert.DoesNotContain(RivalUrl, TextOf(result.Sections[0].Paragraphs[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sentence_whose_figures_sit_on_different_pages_is_left_uncited_and_says_so()
    {
        var body = Section("Both", Text("Customers saved 200,000 hours on plans from $49 a month."));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        Assert.Empty(result.Placed);
        Assert.Equal(GccFigureSources.OnDifferentPages, Assert.Single(result.Uncited).Why);
    }

    [Fact]
    public void A_figure_in_what_the_writer_was_shown_but_on_no_page_is_left_uncited()
    {
        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Odd", Text("It handles 9,999 invoices."))], Inputs());

        Assert.Empty(result.Placed);
        Assert.Equal(GccFigureSources.OnNoPage, Assert.Single(result.Uncited).Why);
    }

    [Fact]
    public void The_publishers_own_page_is_cited_under_the_publishers_name()
    {
        var inputs = Inputs(
            partners: [],
            site: [Page(OwnUrl, "Our sales tax guide", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "We have helped 45 clients with nexus reviews.")]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Us", Text("We have helped 45 clients so far."))], inputs);

        Assert.Equal("We have helped 45 clients so far (Geek @ Your Spot, as of Oct 1, 2026).", TextOf(result.Sections[0].Paragraphs[0]));
        Assert.Equal(OwnUrl, Assert.Single(result.Placed).Url);
    }

    [Fact]
    public void A_page_of_no_partner_and_not_the_publishers_is_cited_by_its_host()
    {
        var inputs = Inputs(partners: [Page("https://help.example.org/limits", "Limits", null, "The limit is 12 seats.")]);

        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Limits", Text("The plan allows 12 seats."))], inputs);

        Assert.Equal("The plan allows 12 seats (help.example.org).", TextOf(result.Sections[0].Paragraphs[0]));
    }

    [Fact]
    public void The_opening_is_cited_like_the_body()
    {
        var result = GccFigureSources.Cite(Opening("Teams have saved 200,000 hours."), [Section("Body", Text("The close runs."))], Inputs());

        Assert.Equal("Teams have saved 200,000 hours (Zamp, as of Oct 13, 2026).", TextOf(result.Lede.Paragraphs[0]));
        Assert.Equal("Opening", Assert.Single(result.Placed).Heading);
    }

    [Fact]
    public void A_sentence_under_a_subsection_is_cited_and_named_for_that_heading()
    {
        var child = Section("How many hours", Text("Customers report 200,000 hours saved."));
        var parent = new Section("h2", "Results", [Text("The results vary.")], null, [child]);

        var result = GccFigureSources.Cite(Opening("An opening."), [parent], Inputs());

        Assert.Equal("Customers report 200,000 hours saved (Zamp, as of Oct 13, 2026).", TextOf(result.Sections[0].Children[0].Paragraphs[0]));
        Assert.Equal("How many hours", Assert.Single(result.Placed).Heading);
    }

    [Fact]
    public void A_linked_run_is_not_split_and_a_citation_after_it_lands_after_the_link()
    {
        // The tool linker put Zamp's tool page on its name at the end of the sentence; the full stop is
        // in the run after it, so the citation sits between the link and the stop.
        var body = Section("Who", Text(
            new Run("200,000 hours saved is the headline of "),
            new Run("Zamp", Href: "/tools/accounting/sales-tax/zamp"),
            new Run(". The rest follows.")));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        var runs = Runs(result.Sections[0].Paragraphs[0]);
        Assert.Equal(
            "200,000 hours saved is the headline of Zamp (Zamp, as of Oct 13, 2026). The rest follows.",
            string.Concat(runs.Select(run => run.Text)));
        Assert.Equal("/tools/accounting/sales-tax/zamp", runs.Single(run => run.Text == "Zamp" && run.Href is { } href && href.StartsWith('/')).Href);
        Assert.Equal("Zamp", runs.Single(run => run.Href == ZampUrl).Text);
    }

    [Fact]
    public void A_sentence_ending_in_a_question_mark_or_a_closing_quotation_mark_keeps_its_mark_after_the_citation()
    {
        var body = Section("Asked", Text("Did they really save 200,000 hours? Their page says \"200,000 hours saved.\""));

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        Assert.Equal(
            "Did they really save 200,000 hours (Zamp, as of Oct 13, 2026)? Their page says \"200,000 hours saved.\"",
            TextOf(result.Sections[0].Paragraphs[0]));
    }

    [Fact]
    public void A_sentence_with_no_terminal_mark_is_cited_at_its_end()
    {
        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Bare", Text("200,000 hours saved"))], Inputs());

        Assert.Equal("200,000 hours saved (Zamp, as of Oct 13, 2026)", TextOf(result.Sections[0].Paragraphs[0]));
    }

    [Fact]
    public void A_quotation_a_heading_and_a_year_in_a_cite_are_not_read()
    {
        var quote = new QuoteParagraph([new Run("200,000 hours saved")], Cite: ZampUrl);
        var body = new Section("h2", "7 reasons 200,000 matters", [quote, Text("Nothing numeric here.")], null, []);

        var result = GccFigureSources.Cite(Opening("An opening."), [body], Inputs());

        Assert.Empty(result.Placed);
        Assert.Empty(result.Uncited);
        Assert.Same(body, result.Sections[0]);
    }

    [Fact]
    public void A_page_with_no_figure_comes_back_as_it_was()
    {
        var opening = Opening("An opening.");
        var body = Section("Plain", Text("Nothing to cite here."));

        var result = GccFigureSources.Cite(opening, [body], Inputs());

        Assert.Same(opening, result.Lede);
        Assert.Same(body, result.Sections[0]);
        Assert.Empty(result.Placed);
        Assert.Empty(result.Uncited);
        Assert.Null(GccFigureSources.Line("pillar", result));
    }

    [Fact]
    public void The_operators_line_names_each_uncited_sentence_with_its_figures_heading_and_reason()
    {
        var inputs = Inputs(operatorsWords: "under 60 seconds");
        var result = GccFigureSources.Cite(
            Opening("An opening."),
            [
                Section("Speed", Text("A close takes under 60 seconds.")),
                Section("Pricing", Text("Zamp starts at $49 a month.")),
            ],
            inputs);

        var line = GccFigureSources.Line("blog", result);

        Assert.Equal(
            "The blog: 2 sentences state a figure with no page to cite it to: "
            + "\"A close takes under 60 seconds.\" (60 seconds), under \"Speed\": the figure is the operator's own, from the Brief, and has no page to cite; "
            + "\"Zamp starts at $49 a month.\" ($49), under \"Pricing\": the sentence names Zamp, whose pages do not state it; it is on Quaderno's page. "
            + "Every other figure in the opening and the body is cited to the page it is on.",
            line);
    }

    [Fact]
    public void One_uncited_sentence_reads_in_the_singular()
    {
        var result = GccFigureSources.Cite(Opening("An opening."), [Section("Odd", Text("It handles 9,999 invoices."))], Inputs());

        Assert.StartsWith("The tool page: 1 sentence states a figure with no page to cite it to: \"It handles 9,999 invoices.\" (9,999 invoices), under \"Odd\": ", GccFigureSources.Line("tool page", result), StringComparison.Ordinal);
    }
}
