using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Which passages of research a request of a pillar or a blog carries
/// (plans/research-classification.md). Research that fits is carried whole. Above the ceiling a choice
/// is made for each request's sections and recorded; a blog's requests are sent it (2026-10-12, on
/// Jeff's word), and a pillar's are still sent everything.
/// </summary>
public class GccResearchSelectionTests
{
    private static readonly GccPassageSearch TheProblem = new(GccPassageSearch.KindProblem, "Cash arrives whenever the customer pays.");

    private static readonly GccPassageSearch ARow = new(
        GccPassageSearch.KindRow, "Scheduled reminders by email.", Failure: "Reminders are sent when someone remembers.");

    private static GccQuoteablePage Page(string url, params string[] passages) =>
        new(url, "Title", [], passages, RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

    private static IReadOnlyList<string> Labels(GccQuoteablePage page, int paragraph = 0) =>
        [.. GccPassageClassifier.LabelsOf(page, paragraph).Keys];

    // ---- how a passage gets its label ---------------------------------------------------------------

    [Fact]
    public void A_passage_on_a_pricing_page_is_about_fit_and_price_whatever_it_says()
    {
        var page = Page("https://partner.test/pricing", "Everything a growing company needs.") with { Category = "pricing" };

        Assert.Equal([GccResearchLabel.FitPriceAndLimits], Labels(page));
        Assert.Equal([GccResearchLabel.FitPriceAndLimits], Labels(page with { Category = "compare" }));
    }

    [Fact]
    public void A_passage_the_briefs_problem_found_is_about_the_problem()
    {
        var page = GccGroundingResolver.FoundBy(Page("https://partner.test/a", "Customers pay on their own schedule."), TheProblem);

        Assert.Equal([GccResearchLabel.Problem], Labels(page));
    }

    [Fact]
    public void A_passage_a_rows_search_found_is_about_how_it_works_and_what_its_remedy_names()
    {
        var plain = GccGroundingResolver.FoundBy(Page("https://partner.test/a", "Customers are nudged on a schedule."), ARow);
        Assert.Equal([GccResearchLabel.HowItWorks], Labels(plain));

        // A remedy that names a connection leans the passage that way; the passage's own word settles it.
        var connects = new GccPassageSearch(
            GccPassageSearch.KindRow, "Bank feeds sync balances every night.", Failure: "Balances are typed in by hand.", Terms: "bank feed");
        var found = GccGroundingResolver.FoundBy(Page("https://partner.test/b", "Balances arrive through a direct connector."), connects);
        Assert.Equal([GccResearchLabel.HowItWorks, GccResearchLabel.DataAndIntegrations], Labels(found));
        // The failure's own words are not read: every failure would say "problem".
        Assert.DoesNotContain(GccResearchLabel.Problem, Labels(found));
    }

    [Fact]
    public void One_common_word_gives_no_label_and_two_of_a_labels_words_give_it()
    {
        Assert.Empty(Labels(Page("https://partner.test/a", "Your team will love it.")));
        Assert.Equal(
            [GccResearchLabel.PeopleAndRoles],
            Labels(Page("https://partner.test/a", "Your finance team and every manager see the same numbers.")));
    }

    [Fact]
    public void A_word_and_its_other_forms_are_one_word()
    {
        // The 14:20 UTC run of 2026-10-11: "clients" and "each client" gave people and roles from one
        // word, and "finance team" was counted as "team" and again as itself.
        Assert.Empty(Labels(Page("https://partner.test/a", "Each client sees what the other clients see.")));
        Assert.Empty(Labels(Page("https://partner.test/a", "The finance team is one of several teams.")));
        Assert.Empty(Labels(Page("https://partner.test/a", "Setup is quick: set up once and you are done.")));
        Assert.Empty(Labels(Page("https://partner.test/a", "Integrations integrate with every integration.")));
        // Two different words still give the label.
        Assert.Equal(
            [GccResearchLabel.PeopleAndRoles],
            Labels(Page("https://partner.test/a", "Each client and its accountant see the same numbers.")));
    }

    [Fact]
    public void A_word_the_subject_uses_for_its_own_things_gives_no_label()
    {
        // The same run's research was about cash flow forecasting, where a plan is a financial plan and
        // a timeline is a forecast's. 17 passages were labelled fit and price by "plan" and "plans".
        Assert.DoesNotContain(
            GccResearchLabel.FitPriceAndLimits,
            Labels(Page("https://partner.test/a", "Build a plan, compare it with last year's plans.") with { Category = "blog" }));
        Assert.Empty(Labels(Page("https://partner.test/a", "Implementing a rolling forecast gives a thirteen-week timeline.")));
        // And everyday words that were read as proof: seven passages, none of them a customer's result.
        Assert.Empty(Labels(Page("https://partner.test/a", "Users review the results faster, with increased confidence.")));
    }

    [Fact]
    public void A_product_page_leans_a_passage_toward_how_it_works_without_settling_it()
    {
        var product = Page("https://partner.test/product", "Nothing here names anything.") with { Category = "product" };
        Assert.Empty(Labels(product));

        var withAWord = product with { Paragraphs = ["The dashboard shows where you stand."] };
        Assert.Equal([GccResearchLabel.HowItWorks], Labels(withAWord));
    }

    [Fact]
    public void A_blog_post_an_about_page_and_an_unmarked_page_say_nothing_by_their_kind()
    {
        foreach (var kind in new[] { "blog", "about", "careers", "legal", "page", null })
        {
            Assert.Empty(Labels(Page("https://partner.test/a", "Nothing here names anything.") with { Category = kind }));
        }
    }

    [Fact]
    public void A_word_is_matched_whole_and_through_punctuation_and_case()
    {
        // "pricey" is not "price", "apiary" is not "api", "comparisons" is not "comparison".
        Assert.Empty(Labels(Page("https://partner.test/a", "A pricey apiary takes a season of comparisons.")));
        Assert.Equal(
            [GccResearchLabel.GettingItRunning],
            Labels(Page("https://partner.test/a", "Go-Live in days: ONBOARDING is guided.")));
        Assert.Equal(" go live in days ", GccPassageClassifier.Normalize("Go-Live, in days!"));
        Assert.Equal(" ", GccPassageClassifier.Normalize(null));
    }

    [Fact]
    public void The_line_of_link_text_under_a_passage_is_not_read()
    {
        // A section's links are often the site's own menu, and would label every passage by it.
        var menu = $"Forecasts update as the bank clears.\n{HttpGeekCrawlerRagClient.LinkedFromLabel} Pricing, Plans, Integrations, API, Case studies, Reviews";

        Assert.Empty(Labels(Page("https://partner.test/a", menu)));
    }

    [Fact]
    public void A_passage_may_carry_more_than_one_label_each_with_its_strength()
    {
        var page = GccGroundingResolver.FoundBy(
            Page("https://partner.test/pricing", "Priced per month, and setup with onboarding is included.") with { Category = "pricing" },
            TheProblem);

        var labels = GccPassageClassifier.LabelsOf(page, 0);

        Assert.Equal(2, labels[GccResearchLabel.Problem]);
        Assert.Equal(2, labels[GccResearchLabel.GettingItRunning]);
        // The kind of page and two of its own words.
        Assert.Equal(4, labels[GccResearchLabel.FitPriceAndLimits]);
        Assert.Equal(3, labels.Count);
    }

    [Fact]
    public void A_place_the_page_has_no_passage_at_has_no_label()
    {
        var page = Page("https://partner.test/pricing", "Plans start per month.") with { Category = "pricing" };

        Assert.Empty(GccPassageClassifier.LabelsOf(page, 1));
        Assert.Empty(GccPassageClassifier.LabelsOf(page, -1));
    }

    [Fact]
    public void The_word_lists_name_no_subject()
    {
        // The words any vendor's pages use for these seven things. A list that named one project's
        // subject would label that project's research and misread the next: the tool page's outline
        // named "approval chains, routing" for every subject until 2026-10-11.
        string[] subjects =
        [
            "invoice", "invoices", "payable", "payables", "receivable", "receivables", "approval", "approvals",
            "spreadsheet", "spreadsheets", "cash flow", "forecast", "forecasting", "payment", "payments", "payroll",
            // A forecasting tool's own: a financial plan, a forecast's timeline (the 14:20 UTC run of 2026-10-11).
            "plan", "plans", "timeline",
        ];

        Assert.Equal(GccResearchLabel.All.Order(), GccPassageClassifier.Words.Keys.Order());
        foreach (var (label, families) in GccPassageClassifier.Words)
        {
            var words = families.SelectMany(family => family).ToList();
            Assert.All(words, word => Assert.DoesNotContain(word, subjects));
            // Each as it reads once a text is normalised, or it can never be found.
            Assert.All(words, word => Assert.Equal($" {word} ", GccPassageClassifier.Normalize(word)));
            Assert.Equal(words.Count, words.Distinct(StringComparer.Ordinal).Count());
            Assert.All(families, family => Assert.NotEmpty(family));
            Assert.NotEmpty(label);
        }
    }

    [Fact]
    public void No_word_of_a_label_is_counted_as_two()
    {
        // A phrase that holds another family's word would give the label on its own: "finance team" was
        // "team" and "finance team", two of people and roles' words, until they were made one family.
        foreach (var (label, families) in GccPassageClassifier.Words)
        {
            for (var i = 0; i < families.Length; i++)
            {
                foreach (var form in families[i])
                {
                    var reads = GccPassageClassifier.Normalize(form);
                    for (var j = 0; j < families.Length; j++)
                    {
                        if (j == i) continue;
                        Assert.All(
                            families[j],
                            other => Assert.False(
                                reads.Contains($" {other} ", StringComparison.Ordinal),
                                $"{label}: \"{form}\" holds \"{other}\", a word of another family."));
                    }
                }
            }
        }
    }

    // ---- which section is written from which label --------------------------------------------------

    [Fact]
    public void The_pillars_sections_name_the_part_of_the_research_each_is_written_from()
    {
        Assert.Equal(
            [
                GccResearchLabel.Problem,
                GccResearchLabel.Problem,
                GccResearchLabel.Problem,
                GccResearchLabel.HowItWorks,
                GccResearchLabel.GettingItRunning,
                GccResearchLabel.GettingItRunning,
                GccResearchLabel.GettingItRunning,
                GccResearchLabel.DataAndIntegrations,
                GccResearchLabel.PeopleAndRoles,
                GccResearchLabel.FitPriceAndLimits,
                GccResearchLabel.FitPriceAndLimits,
            ],
            PillarPrompts.Outline(null).Select(slot => slot.ResearchLabel));

        // With a framing the sections are the same sections.
        var framed = PillarPrompts.Outline(new GccNicheFraming("A problem.", ["A failure."], "An answer."));
        Assert.Equal(PillarPrompts.Outline(null).Select(slot => slot.ResearchLabel), framed.Select(slot => slot.ResearchLabel));
    }

    [Fact]
    public void The_blogs_sections_name_theirs_and_a_tool_pages_name_none()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder())
            .OutlineFor(new ContentTypePromptContext(GccOpeningAsksNothingTests.Context()));

        Assert.Equal(
            [
                GccResearchLabel.Problem,
                GccResearchLabel.Problem,
                GccResearchLabel.HowItWorks,
                GccResearchLabel.GettingItRunning,
                GccResearchLabel.ResultsAndProof,
                GccResearchLabel.FitPriceAndLimits,
            ],
            blog.Select(slot => slot.ResearchLabel));

        // A tool page carries one partner's research, fits, and is not chosen for: its sections name none,
        // and a slot names none unless it is told to.
        Assert.Null(SectionSlot.Cover("what the product does").ResearchLabel);
        Assert.Null(SectionSlot.Assigned("A heading").ResearchLabel);
    }

    [Fact]
    public void Every_label_a_section_names_is_one_of_the_seven()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder())
            .OutlineFor(new ContentTypePromptContext(GccOpeningAsksNothingTests.Context()));

        Assert.All(
            PillarPrompts.Outline(null).Concat(blog),
            slot => Assert.Contains(slot.ResearchLabel, GccResearchLabel.All));
        Assert.DoesNotContain(GccResearchLabel.General, GccResearchLabel.All);
    }

    // ---- what a request would carry -----------------------------------------------------------------

    /// <summary>One passage of <paramref name="chars"/> characters, labelled by the kind of search that found it.</summary>
    private static GccResearchPassage Passage(
        string host, int page, int position, int chars, string? label, double score = 0.5, int strength = 2) =>
        new(
            host,
            $"https://{host}/{page}",
            position,
            chars,
            PageChars: 100,
            label is null ? new Dictionary<string, int>() : new Dictionary<string, int> { [label] = strength },
            FoundBySearches: 1,
            score);

    private static readonly IReadOnlyDictionary<(string Url, int Position), int> NothingShown =
        new Dictionary<(string Url, int Position), int>();

    /// <summary>Five partners, each with far more than its limit: 30 passages of 4,900 for each of two labels.</summary>
    private static IReadOnlyList<GccResearchPassage> FivePartnersOverTheLimit()
    {
        var passages = new List<GccResearchPassage>();
        foreach (var host in new[] { "a.test", "b.test", "c.test", "d.test", "e.test" })
        {
            for (var i = 0; i < 30; i++)
            {
                passages.Add(Passage(host, i, 0, 4_900, i % 2 == 0 ? GccResearchLabel.Problem : GccResearchLabel.HowItWorks, score: 1.0 - i / 100.0));
            }
        }

        return passages;
    }

    [Fact]
    public void Research_that_fits_under_the_ceiling_is_carried_whole()
    {
        // 300,300 characters: more than a chosen request would carry, and still left alone. The 14:20 UTC
        // run of 2026-10-11 was 250,717, and choosing from it would have cost more to send less.
        var passages = new List<GccResearchPassage>
        {
            Passage("a.test", 1, 0, 100_000, GccResearchLabel.Problem),
            Passage("a.test", 2, 0, 100_000, GccResearchLabel.FitPriceAndLimits),
            Passage("b.test", 1, 0, 100_000, null),
        };
        Assert.InRange(GccResearchSelector.CharsOf(passages), GccResearchSelector.MaxCharsInAll + 1, GccResearchSelector.WholeUpToChars);

        var selection = GccResearchSelector.Select(passages, [GccResearchLabel.HowItWorks], NothingShown);

        Assert.True(selection.Whole);
        Assert.Equal(passages, selection.Chosen.Select(chosen => chosen.Passage));
        Assert.All(selection.Chosen, chosen => Assert.Equal(GccResearchSelector.WhyWhole, chosen.Why));
        Assert.Empty(selection.LeftOut);

        // One character over the ceiling and it is chosen from.
        var over = new List<GccResearchPassage>(passages) { Passage("b.test", 2, 0, 49_601, null) };
        Assert.Equal(GccResearchSelector.WholeUpToChars + 1, GccResearchSelector.CharsOf(over));
        Assert.False(GccResearchSelector.Select(over, [GccResearchLabel.HowItWorks], NothingShown).Whole);
    }

    [Fact]
    public void Over_the_limit_every_partner_is_kept_and_none_passes_its_limit()
    {
        var passages = FivePartnersOverTheLimit();

        var selection = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);

        Assert.False(selection.Whole);
        var carried = selection.Chosen.Select(chosen => chosen.Passage).ToList();
        foreach (var host in new[] { "a.test", "b.test", "c.test", "d.test", "e.test" })
        {
            var mine = carried.Where(passage => passage.Host == host).ToList();
            Assert.NotEmpty(mine);
            Assert.InRange(GccResearchSelector.CharsOf(mine), 1, GccResearchSelector.MaxCharsPerPartner);
            // Every one is labelled for the request: a partner with enough of those has no room for others.
            Assert.All(mine, passage => Assert.True(passage.Labels.ContainsKey(GccResearchLabel.Problem)));
            // 4,900 and its page's 100 lines, eight times, is the limit exactly.
            Assert.Equal(8, mine.Count);
            // The search's best first.
            Assert.Equal(mine.OrderByDescending(passage => passage.Score), mine);
        }

        Assert.InRange(GccResearchSelector.CharsOf(carried), 1, GccResearchSelector.MaxCharsInAll);
        Assert.Equal(passages.Count, selection.Chosen.Count + selection.LeftOut.Count);
        Assert.All(selection.Chosen, chosen => Assert.Equal(GccResearchSelector.WhyLabelled, chosen.Why));
    }

    [Fact]
    public void The_same_passages_give_the_same_choice()
    {
        var first = GccResearchSelector.Select(FivePartnersOverTheLimit(), [GccResearchLabel.Problem], NothingShown);
        var second = GccResearchSelector.Select(FivePartnersOverTheLimit(), [GccResearchLabel.Problem], NothingShown);

        Assert.Equal(
            first.Chosen.Select(chosen => (chosen.Passage.Url, chosen.Passage.Position, chosen.Why)),
            second.Chosen.Select(chosen => (chosen.Passage.Url, chosen.Passage.Position, chosen.Why)));
    }

    [Fact]
    public void A_partner_with_nothing_for_the_request_is_carried_up_to_its_limit_all_the_same()
    {
        // One partner's research is all about price; the request writes the problem. On the 14:20 UTC run
        // of 2026-10-11 the research held 8 passages of 213 for getting it running, and the request that
        // writes those sections would have been shown a fifth of what the others were.
        var passages = new List<GccResearchPassage>(FivePartnersOverTheLimit().Where(passage => passage.Host != "e.test"));
        for (var i = 0; i < 30; i++)
        {
            passages.Add(Passage("e.test", i, 0, 4_900, GccResearchLabel.FitPriceAndLimits, score: 1.0 - i / 100.0));
        }

        var selection = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);

        var others = selection.Chosen.Where(chosen => chosen.Passage.Host == "e.test").ToList();
        Assert.All(others, chosen => Assert.Equal(GccResearchSelector.WhyOther, chosen.Why));
        // As much as any partner: eight passages reach the limit, the search's best first.
        Assert.Equal(GccResearchSelector.MaxCharsPerPartner, GccResearchSelector.CharsOf(others.Select(chosen => chosen.Passage)));
        Assert.Equal(
            Enumerable.Range(0, 8),
            others.Select(chosen => int.Parse(new Uri(chosen.Passage.Url).AbsolutePath.Trim('/'))));
    }

    [Fact]
    public void Of_a_partners_other_passages_the_ones_carried_least_come_first()
    {
        var passages = new List<GccResearchPassage>(FivePartnersOverTheLimit().Where(passage => passage.Host != "e.test"));
        for (var i = 0; i < 30; i++)
        {
            passages.Add(Passage("e.test", i, 0, 4_900, GccResearchLabel.FitPriceAndLimits, score: 1.0 - i / 100.0));
        }

        var first = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);
        var shown = first.Chosen.ToDictionary(chosen => (chosen.Passage.Url, chosen.Passage.Position), _ => 1);
        var second = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], shown);

        // The next request for the same sections is given the eight after the first eight.
        Assert.Equal(
            Enumerable.Range(8, 8),
            second.Chosen
                .Where(chosen => chosen.Passage.Host == "e.test")
                .Select(chosen => int.Parse(new Uri(chosen.Passage.Url).AbsolutePath.Trim('/'))));
    }

    [Fact]
    public void The_requests_own_passages_come_first_then_general_ones_then_the_partners_others()
    {
        IReadOnlyList<GccChosenPassage> OfA(int chars)
        {
            var passages = new List<GccResearchPassage>(FivePartnersOverTheLimit().Where(passage => passage.Host != "a.test"))
            {
                Passage("a.test", 1, 0, chars, GccResearchLabel.FitPriceAndLimits, score: 0.99),
                Passage("a.test", 2, 0, chars, null, score: 0.10),
                Passage("a.test", 3, 0, chars, GccResearchLabel.Problem, score: 0.20),
                Passage("a.test", 4, 0, chars, GccResearchLabel.Problem, score: 0.30, strength: 4),
            };

            return [.. GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown).Chosen
                .Where(chosen => chosen.Passage.Host == "a.test")];
        }

        // All four fit the partner's limit: the stronger label, the other labelled passage, the general
        // one, and last the passage about price, however well the search scored it.
        var all = OfA(9_900);
        Assert.Equal(
            ["https://a.test/4", "https://a.test/3", "https://a.test/2", "https://a.test/1"],
            all.Select(chosen => chosen.Passage.Url));
        Assert.Equal(
            [GccResearchSelector.WhyLabelled, GccResearchSelector.WhyLabelled, GccResearchSelector.WhyGeneral, GccResearchSelector.WhyOther],
            all.Select(chosen => chosen.Why));

        // Room for three: the passage for another request's sections is the one left out.
        Assert.Equal(
            ["https://a.test/4", "https://a.test/3", "https://a.test/2"],
            OfA(13_000).Select(chosen => chosen.Passage.Url));
    }

    [Fact]
    public void A_passage_an_earlier_request_carried_counts_half_so_the_next_request_reaches_further()
    {
        var passages = FivePartnersOverTheLimit();
        var first = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);
        var shown = first.Chosen.ToDictionary(chosen => (chosen.Passage.Url, chosen.Passage.Position), _ => 1);

        var second = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], shown);

        // Fifteen passages of each partner are about the problem and eight fit. The second request for
        // the same label is given the seven the first was not, and then the best of the eight it was.
        var firstOfA = first.Chosen.Where(c => c.Passage.Host == "a.test").Select(c => c.Passage.Url).ToList();
        var secondOfA = second.Chosen.Where(c => c.Passage.Host == "a.test").Select(c => c.Passage.Url).ToList();
        Assert.Equal(8, secondOfA.Count);
        Assert.Equal(7, secondOfA.Except(firstOfA).Count());
        Assert.Equal(firstOfA[0], Assert.Single(secondOfA.Intersect(firstOfA)));

        Assert.Equal(2, GccResearchSelector.ValueOf(passages[0], [GccResearchLabel.Problem], NothingShown));
        Assert.Equal(1, GccResearchSelector.ValueOf(passages[0], [GccResearchLabel.Problem], shown));
        Assert.Equal(0, GccResearchSelector.ValueOf(passages[1], [GccResearchLabel.Problem], NothingShown));
    }

    [Fact]
    public void With_many_partners_each_has_an_equal_part_of_the_limit_in_all()
    {
        var passages = new List<GccResearchPassage>();
        for (var partner = 0; partner < 8; partner++)
        {
            for (var i = 0; i < 20; i++) passages.Add(Passage($"p{partner}.test", i, 0, 4_900, GccResearchLabel.Problem));
        }

        var selection = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);

        // 240,000 over eight partners is 30,000 each: six passages of 5,000 with their page's lines.
        Assert.All(
            selection.Chosen.GroupBy(chosen => chosen.Passage.Host),
            partner => Assert.Equal(6, partner.Count()));
        Assert.InRange(
            GccResearchSelector.CharsOf(selection.Chosen.Select(chosen => chosen.Passage)), 1, GccResearchSelector.MaxCharsInAll);
    }

    [Fact]
    public void A_passage_that_does_not_fit_is_passed_over_for_a_shorter_one_that_does()
    {
        var passages = new List<GccResearchPassage>(FivePartnersOverTheLimit().Where(passage => passage.Host != "a.test"))
        {
            Passage("a.test", 1, 0, 30_000, GccResearchLabel.Problem, score: 0.9),
            Passage("a.test", 2, 0, 30_000, GccResearchLabel.Problem, score: 0.8),
            Passage("a.test", 3, 0, 5_000, GccResearchLabel.Problem, score: 0.7),
        };

        var selection = GccResearchSelector.Select(passages, [GccResearchLabel.Problem], NothingShown);

        Assert.Equal(
            ["https://a.test/1", "https://a.test/3"],
            selection.Chosen.Where(chosen => chosen.Passage.Host == "a.test").Select(chosen => chosen.Passage.Url));
    }

    [Fact]
    public void A_pages_own_lines_are_counted_once_however_many_of_its_passages_are_carried()
    {
        var onOnePage = new[]
        {
            Passage("a.test", 1, 0, 1_000, null),
            Passage("a.test", 1, 1, 1_000, null),
            Passage("a.test", 2, 0, 1_000, null),
        };

        Assert.Equal(3_200, GccResearchSelector.CharsOf(onOnePage));
        Assert.Equal(0, GccResearchSelector.CharsOf([]));
    }

    [Fact]
    public void The_passages_are_the_ones_the_research_block_prints_and_cost_what_it_prints()
    {
        // Seven passages on the page: the block prints a page's first six, and so does this.
        var first = new GccQuoteablePage(
            "https://partner.test/a", "First page", [new HeadingDto(2, "A heading")],
            ["One.", "Two.", "Three.", "Four.", "Five.", "Six.", "Seven."],
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);
        var second = Page("https://www.partner.test/b", "Context: the block\nSpecific detail: the sentence", "Another.");
        GccCreateDto With(params GccQuoteablePage[] pages) => new(
            Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
            StartingContentType: "pillar", Topic: "Topic", Notes: null, ProjectSiteRunId: null, SiteSectionJson: null,
            BriefJson: null, ResearchJson: GccResearchFetchService.Serialize(new GccResearchDocument(null, pages)),
            Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

        var passages = GccResearchSelector.PassagesOf([first, second]);

        Assert.Equal(8, passages.Count);
        Assert.DoesNotContain(passages, passage => passage.Url == first.Url && passage.Position == 6);
        Assert.All(passages, passage => Assert.Equal("partner.test", passage.Host));
        // What the second page adds to the block a writer is sent is what it is counted as here.
        var withBoth = GccGenerateService.BuildResearchBlock(With(first, second)).Length;
        var withFirst = GccGenerateService.BuildResearchBlock(With(first)).Length;
        Assert.Equal(withBoth - withFirst, GccResearchSelector.CharsOf(passages.Where(passage => passage.Url == second.Url)));
        var withSecond = GccGenerateService.BuildResearchBlock(With(second)).Length;
        Assert.Equal(withBoth - withSecond, GccResearchSelector.CharsOf(passages.Where(passage => passage.Url == first.Url)));

        Assert.Empty(GccResearchSelector.PassagesOf(null));
        Assert.Empty(GccResearchSelector.PassagesOf([Page("https://partner.test/empty")]));
    }

    /// <summary>
    /// Every partner has the same limit, so what counts as one partner decides how much of it a request
    /// carries. A partner's help or documentation site is that partner's, not a partner of its own.
    /// </summary>
    [Fact]
    public void A_page_on_a_partners_subdomain_is_that_partners()
    {
        GccQuoteablePage[] pages =
        [
            Page("https://www.partner.test/product", "One."),
            Page("https://help.partner.test/setup", "Two."),
            Page("https://docs.app.other.test/api", "Three."),
            Page("https://notpartner.test/post", "Four."),
        ];

        // With no partners given a page's partner is its own host, as before.
        Assert.Equal(
            ["partner.test", "help.partner.test", "docs.app.other.test", "notpartner.test"],
            GccResearchSelector.PassagesOf(pages).Select(passage => passage.Host));

        var passages = GccResearchSelector.PassagesOf(
            pages, ["https://www.partner.test/us", "https://other.test/", "https://app.other.test/", "not an address"]);

        Assert.Equal(
            // Of two partners a host is under, the longer one. A host that only ends in a partner's
            // letters is not under it.
            ["partner.test", "partner.test", "app.other.test", "notpartner.test"],
            passages.Select(passage => passage.Host));
        Assert.Equal("partner.test", GccResearchSelector.PartnerOf("https://HELP.Partner.test/x", ["partner.test"]));
        Assert.Equal(string.Empty, GccResearchSelector.PartnerOf(null, ["partner.test"]));
    }

    [Fact]
    public void Research_read_back_with_parts_missing_is_counted_as_it_is_and_stops_nothing()
    {
        // Stored research is JSON, and a field the record calls required can be absent from it. This
        // runs on every pillar and blog while it only watches, and must never be what refuses a page.
        var stored = GccResearchFetchService.Deserialize(
            """
            {"serpIndex":null,"quoteables":[
              {"url":null,"title":null,"headings":null,"paragraphs":["One.",null]},
              {"url":"https://partner.test/a","title":"A","headings":[{"level":2,"text":null}],"paragraphs":["Two."],
               "searches":[null],"foundBy":[null]},
              {"url":"https://partner.test/b","title":"B","headings":[],"paragraphs":null},
              null
            ]}
            """)!;

        var passages = GccResearchSelector.PassagesOf(stored.Quoteables);

        Assert.Equal(3, passages.Count);
        Assert.Equal(["", "", "partner.test"], passages.Select(passage => passage.Host));
        Assert.All(passages, passage => Assert.Empty(passage.Labels));
        var forRequest = For(Sending(stored.Quoteables), ProblemSections, "Pillar body sections 1-2", 1, 5)!;
        var record = JsonSerializer.SerializeToElement(forRequest.Record);
        Assert.Equal(3, record.GetProperty("research").GetProperty("passages").GetInt32());
        // It fits, so the request is sent the research as it is and nothing is chosen from it.
        Assert.Null(forRequest.Carried);
        Assert.Empty(stored.Quoteables[1].SearchesThatFound(0));
    }

    // ---- what a request carries, and the record of it ------------------------------------------------

    /// <summary>A page whose requests are sent what is chosen for them: the blog's, and the pillar's.</summary>
    private static GccResearchPerRequest Sending(IReadOnlyList<GccQuoteablePage>? pages) =>
        new(pages, null, sendsWhatIsChosen: true);

    /// <summary>A page whose requests' choices are recorded and which are sent everything.</summary>
    private static GccResearchPerRequest Recording(IReadOnlyList<GccQuoteablePage>? pages) =>
        new(pages, null, sendsWhatIsChosen: false);

    private static GccRequestResearch? For(
        GccResearchPerRequest research, IReadOnlyList<SectionSlot> sections, string request, int call, int of) =>
        research.For(GccResearchPerRequest.LabelsOf(sections), request, call, of);

    private static JsonElement RecordOf(
        GccResearchPerRequest research, IReadOnlyList<SectionSlot> sections, string request, int call, int of) =>
        JsonSerializer.SerializeToElement(For(research, sections, request, call, of)!.Record);

    private static GccCreateDto CreateWith(IReadOnlyList<GccQuoteablePage> pages) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "blog", Topic: "Topic", Notes: null, ProjectSiteRunId: null, SiteSectionJson: null,
        BriefJson: null, ResearchJson: GccResearchFetchService.Serialize(new GccResearchDocument(null, pages)),
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    /// <summary>Two partners with more than the ceiling between them, on pages the classifier labels.</summary>
    private static IReadOnlyList<GccQuoteablePage> TwoPartnersOverTheLimit()
    {
        var filler = new string('x', 2_900);
        var pages = new List<GccQuoteablePage>();
        foreach (var host in new[] { "a.test", "b.test" })
        {
            for (var i = 0; i < 70; i++)
            {
                var page = Page($"https://{host}/{i}", $"Passage {i} of {host}. {filler}");
                pages.Add(i % 2 == 0
                    ? GccGroundingResolver.FoundBy(page, TheProblem)
                    : page with { Category = "pricing" });
            }
        }

        return pages;
    }

    private static readonly IReadOnlyList<SectionSlot> ProblemSections =
        [.. PillarPrompts.Outline(null).Skip(1).Take(2)];

    [Fact]
    public void A_request_whose_sections_name_no_label_has_no_choice_made_for_it()
    {
        var research = Sending(TwoPartnersOverTheLimit());

        Assert.Null(For(research, [SectionSlot.Cover("what the product does"), SectionSlot.Assigned("A heading")], "Tool body sections 1-2", 1, 5));
        Assert.Null(For(research, [], "Nothing", 1, 1));
    }

    /// <summary>
    /// The switch itself: over the ceiling, a page that sends is handed exactly the passages its record
    /// lists as chosen; a page that only records is handed none and its record says so; and research
    /// that fits is handed to neither, because nothing was chosen from it.
    /// </summary>
    [Fact]
    public void A_request_is_sent_exactly_what_its_record_lists_as_chosen_and_only_on_a_page_that_sends()
    {
        var sent = For(Sending(TwoPartnersOverTheLimit()), ProblemSections, "Blog body sections 1-2", 1, 3)!;
        var sentRecord = JsonSerializer.SerializeToElement(sent.Record);

        Assert.NotNull(sent.Carried);
        Assert.False(sentRecord.GetProperty("watch").GetBoolean());
        Assert.False(sentRecord.GetProperty("whole").GetBoolean());
        Assert.Equal(
            sentRecord.GetProperty("chosen").EnumerateArray()
                .Select(c => (c.GetProperty("url").GetString()!, c.GetProperty("passage").GetInt32() - 1))
                .OrderBy(key => key),
            sent.Carried.OrderBy(key => key));
        Assert.DoesNotContain(
            sentRecord.GetProperty("leftOut").EnumerateArray(),
            l => sent.Carried.Contains((l.GetProperty("url").GetString()!, l.GetProperty("passage").GetInt32() - 1)));

        var recorded = For(Recording(TwoPartnersOverTheLimit()), ProblemSections, "Pillar body sections 1-2", 1, 5)!;
        var recordedRecord = JsonSerializer.SerializeToElement(recorded.Record);

        Assert.Null(recorded.Carried);
        Assert.True(recordedRecord.GetProperty("watch").GetBoolean());
        // The same choice either way: only whether it is sent differs.
        Assert.Equal(
            sentRecord.GetProperty("chosen").GetRawText(),
            recordedRecord.GetProperty("chosen").GetRawText());

        var fits = For(Sending([.. TwoPartnersOverTheLimit().Take(10)]), ProblemSections, "Blog body sections 1-2", 1, 3)!;
        Assert.Null(fits.Carried);
        Assert.False(JsonSerializer.SerializeToElement(fits.Record).GetProperty("watch").GetBoolean());
    }

    /// <summary>
    /// The block a request is sent when its research was chosen for it: the chosen passages under their
    /// pages, none of the others, and a line saying it is a part. It costs what the selector counted.
    /// </summary>
    [Fact]
    public void The_research_block_prints_a_requests_choice_says_it_is_a_part_and_costs_what_was_counted()
    {
        var pages = TwoPartnersOverTheLimit();
        var create = CreateWith(pages);
        var carried = For(Sending(pages), ProblemSections, "Blog body sections 1-2", 1, 3)!.Carried!;
        var all = GccResearchSelector.PassagesOf(pages);

        var whole = GccGenerateService.BuildResearchBlock(create);
        var part = GccGenerateService.BuildResearchBlock(create, carried);

        Assert.DoesNotContain(GccGenerateService.ResearchIsAPartNote, whole, StringComparison.Ordinal);
        Assert.Contains(GccGenerateService.ResearchIsAPartNote, part, StringComparison.Ordinal);
        foreach (var page in pages)
        {
            var isCarried = carried.Contains((page.Url, 0));
            Assert.Equal(isCarried, part.Contains(page.Paragraphs[0], StringComparison.Ordinal));
            Assert.Equal(isCarried, part.Contains($"({page.Url})", StringComparison.Ordinal));
        }

        // Both partners are in it, and neither past its limit.
        Assert.All(new[] { "a.test", "b.test" }, host => Assert.Contains(carried, key => key.Url.Contains(host, StringComparison.Ordinal)));
        var noteLines = GccGenerateService.ResearchIsAPartNote.Length + (2 * Environment.NewLine.Length);
        Assert.Equal(
            GccResearchSelector.CharsOf(all) - GccResearchSelector.CharsOf(all.Where(passage => carried.Contains((passage.Url, passage.Position)))),
            whole.Length - (part.Length - noteLines));
        // Carrying every passage prints what the whole block prints, with the line that says it is a part.
        var everything = all.Select(passage => (passage.Url, passage.Position)).ToHashSet();
        Assert.Equal(whole.Length + noteLines, GccGenerateService.BuildResearchBlock(create, everything).Length);
    }

    [Fact]
    public void The_record_says_what_the_research_holds_what_is_carried_partner_by_partner_and_what_is_left_out()
    {
        var record = RecordOf(Recording(TwoPartnersOverTheLimit()), ProblemSections, "Pillar body sections 1-2", 1, 5);

        Assert.Equal("Pillar body sections 1-2", record.GetProperty("batch").GetString());
        Assert.Equal(1, record.GetProperty("call").GetInt32());
        Assert.Equal(5, record.GetProperty("of").GetInt32());
        Assert.True(record.GetProperty("watch").GetBoolean());
        Assert.False(record.GetProperty("whole").GetBoolean());
        Assert.Equal([GccResearchLabel.Problem], record.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(40_000, record.GetProperty("limits").GetProperty("perPartner").GetInt32());
        Assert.Equal(240_000, record.GetProperty("limits").GetProperty("inAll").GetInt32());
        Assert.Equal(350_000, record.GetProperty("limits").GetProperty("wholeUpTo").GetInt32());

        var held = record.GetProperty("research");
        var carriedSize = record.GetProperty("carried");
        Assert.Equal(140, held.GetProperty("passages").GetInt32());
        Assert.InRange(held.GetProperty("chars").GetInt32(), 350_001, int.MaxValue);
        Assert.InRange(carriedSize.GetProperty("chars").GetInt32(), 1, 80_000);

        var partners = record.GetProperty("partners").EnumerateArray().ToList();
        Assert.Equal(["a.test", "b.test"], partners.Select(p => p.GetProperty("host").GetString()));
        foreach (var partner in partners)
        {
            Assert.Equal(70, partner.GetProperty("research").GetProperty("passages").GetInt32());
            Assert.InRange(partner.GetProperty("carried").GetProperty("chars").GetInt32(), 1, 40_000);
            var carried = partner.GetProperty("carried").GetProperty("passages").GetInt32();
            // The problem's passages fill it: nothing general, and no room for the partner's others.
            Assert.Equal(carried, partner.GetProperty("labelled").GetInt32());
            Assert.Equal(0, partner.GetProperty("general").GetInt32());
            Assert.Equal(0, partner.GetProperty("other").GetInt32());
            Assert.Equal(70 - carried, partner.GetProperty("leftOut").GetInt32());
        }

        var chosen = record.GetProperty("chosen").EnumerateArray().ToList();
        Assert.Equal(carriedSize.GetProperty("passages").GetInt32(), chosen.Count);
        Assert.All(chosen, c => Assert.Equal("labelled", c.GetProperty("why").GetString()));
        Assert.All(chosen, c => Assert.Equal(1, c.GetProperty("passage").GetInt32()));
        Assert.Equal(140 - chosen.Count, record.GetProperty("leftOut").GetArrayLength());
        // A pricing page's passage is named as what it is, for the request that will want it.
        Assert.Contains(
            record.GetProperty("leftOut").EnumerateArray(),
            l => l.GetProperty("labels").EnumerateArray().Any(label => label.GetString() == GccResearchLabel.FitPriceAndLimits));
    }

    [Fact]
    public void A_later_request_is_told_what_the_earlier_ones_carried()
    {
        var research = Sending(TwoPartnersOverTheLimit());
        static IReadOnlyList<string> Chosen(JsonElement record) =>
            [.. record.GetProperty("chosen").EnumerateArray().Select(c => c.GetProperty("url").GetString()!)];

        // The opening, then the body's first request: both state the problem.
        var first = Chosen(RecordOf(research, ProblemSections, "Blog opening", 0, 3));
        var again = Chosen(RecordOf(research, ProblemSections, "Blog body sections 1-2", 1, 3));

        // The same sections asked for again reach passages the first request did not carry.
        Assert.NotEmpty(again.Except(first));
        // A page not yet written has carried nothing, and chooses as the first did.
        Assert.Equal(first, Chosen(RecordOf(Sending(TwoPartnersOverTheLimit()), ProblemSections, "Blog opening", 0, 3)));
    }

    [Fact]
    public void Research_that_fits_is_recorded_as_carried_whole_with_nothing_listed()
    {
        var record = RecordOf(Sending([.. TwoPartnersOverTheLimit().Take(10)]), ProblemSections, "Pillar body sections 1-2", 1, 5);

        Assert.True(record.GetProperty("whole").GetBoolean());
        Assert.False(record.GetProperty("watch").GetBoolean());
        Assert.Equal(
            record.GetProperty("research").GetProperty("chars").GetInt32(),
            record.GetProperty("carried").GetProperty("chars").GetInt32());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("chosen").ValueKind);
        Assert.Equal(JsonValueKind.Null, record.GetProperty("leftOut").ValueKind);
    }

    [Fact]
    public void The_labels_record_counts_every_passage_under_each_label_it_carries_partner_by_partner()
    {
        var record = JsonSerializer.SerializeToElement(Sending(TwoPartnersOverTheLimit()).LabelsRecord());

        Assert.Equal(140, record.GetProperty("passages").GetInt32());
        var byLabel = record.GetProperty("byLabel");
        Assert.Equal(70, byLabel.GetProperty(GccResearchLabel.Problem).GetInt32());
        Assert.Equal(70, byLabel.GetProperty(GccResearchLabel.FitPriceAndLimits).GetInt32());
        Assert.Equal(0, byLabel.GetProperty(GccResearchLabel.PeopleAndRoles).GetInt32());
        Assert.Equal(0, byLabel.GetProperty(GccResearchLabel.General).GetInt32());
        // All seven and "general", so a label with nothing to choose from is seen as nothing.
        Assert.Equal(8, byLabel.EnumerateObject().Count());
        var partners = record.GetProperty("partners").EnumerateArray().ToList();
        Assert.Equal(["a.test", "b.test"], partners.Select(p => p.GetProperty("host").GetString()));
        Assert.All(partners, p => Assert.Equal(35, p.GetProperty("byLabel").GetProperty(GccResearchLabel.Problem).GetInt32()));
    }

    [Fact]
    public void A_page_with_no_research_is_recorded_as_carrying_none()
    {
        var research = Sending(null);

        var forRequest = For(research, ProblemSections, "Pillar body sections 1-2", 1, 5)!;
        var record = JsonSerializer.SerializeToElement(forRequest.Record);

        Assert.Null(forRequest.Carried);
        Assert.True(record.GetProperty("whole").GetBoolean());
        Assert.Equal(0, record.GetProperty("research").GetProperty("passages").GetInt32());
        Assert.Empty(record.GetProperty("partners").EnumerateArray());
        Assert.Equal(0, JsonSerializer.SerializeToElement(research.LabelsRecord()).GetProperty("passages").GetInt32());
    }
}
