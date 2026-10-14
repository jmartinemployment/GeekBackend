using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// "PDF / LinkedIn document": the deck's writer and its checks (<c>plans/enable-pdf.md</c>, step 2).
/// </summary>
/// <remarks>
/// A deck is written through its real writer with a stand-in model
/// (<see cref="BriefFieldReachTests.WriteDeckAsync"/>): the project has two partners, so the deck is six
/// slides, four of them written by the slides request. A test that needs a writer to have written
/// something in particular changes that one reply and nothing else.
/// </remarks>
public sealed class LinkedInDocumentWriterTests(ITestOutputHelper output)
{
    private const string Brief = BriefFieldReachTests.CompleteBrief;

    private static JsonElement Saved(IReadOnlyList<(string Label, string Text)> calls) =>
        JsonDocument.Parse(calls[^1].Text).RootElement;

    private static List<string> Warnings(JsonElement saved) =>
        [.. saved.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()!)];

    /// <summary>The lines of one saved slide after the cover, the first being 0.</summary>
    private static List<string> LinesOf(JsonElement saved, int slide) =>
    [
        .. saved.GetProperty("body").GetProperty("sections")[slide].GetProperty("paragraphs").EnumerateArray()
            .Where(paragraph => paragraph.GetProperty("type").GetString() == "list")
            .SelectMany(paragraph => paragraph.GetProperty("items").EnumerateArray())
            .Select(item => string.Concat(item.EnumerateArray().Select(run => run.GetProperty("text").GetString()))),
    ];

    /// <summary>The reply to one of the deck's requests with one thing in it changed.</summary>
    private static Func<string, string, string> Changing(string request, Action<JsonObject> change) =>
        (label, scripted) =>
        {
            if (label != request) return scripted;
            var reply = (JsonObject)JsonNode.Parse(scripted)!;
            change(reply);
            return reply.ToJsonString();
        };

    private static JsonObject Slide(JsonObject reply, int index) => (JsonObject)((JsonArray)reply["slides"]!)[index]!;

    private static JsonArray Lines(JsonObject reply, int index) => (JsonArray)Slide(reply, index)["lines"]!;

    /// <summary>A deck written with the run's record kept, so what went on it can be read.</summary>
    private static async Task<(IReadOnlyList<(string Label, string Text)> Calls, List<GccGenerateJobEventWrite> Recorded)> WriteRecordedAsync(
        string? research = null,
        Func<string, string, string>? reply = null,
        IReadOnlyList<string>? partnerUrls = null,
        string model = "gpt-4.1",
        Func<GccCompetitorAnalysisResolver>? competitors = null)
    {
        var recorded = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(
            Guid.NewGuid(),
            (events, _) =>
            {
                lock (recorded) recorded.AddRange(events);
                return Task.CompletedTask;
            },
            NullLogger.Instance);
        return (await BriefFieldReachTests.WriteDeckAsync(Brief, research, reply, partnerUrls, model, competitors), recorded);
    }

    private static List<JsonElement> Events(List<GccGenerateJobEventWrite> recorded, string kind) =>
        [.. recorded.Where(e => e.Kind == kind).Select(e => JsonDocument.Parse(e.PayloadJson).RootElement)];

    // ---- what a deck is when it is saved ------------------------------------------------------------

    [Fact]
    public async Task A_deck_is_two_requests_and_is_saved_as_its_slides_its_post_and_the_links_for_the_first_comment()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(Brief);

        Assert.Equal(["cover and post", "slides", "the saved page"], calls.Select(call => call.Label));
        var saved = Saved(calls);

        // The cover is the document's opening and its title is the page's.
        Assert.Equal("The close that runs on one spreadsheet", saved.GetProperty("title").GetString());
        var cover = saved.GetProperty("body").GetProperty("lede");
        Assert.Equal("The close that runs on one spreadsheet", cover.GetProperty("heading").GetString());

        // What each slide is, in order: the cover, the problem, one for each partner in the project's
        // order, the approach, what to do next.
        Assert.Equal(
            ["cover", "problem", "partner:Partner", "partner:Second", "approach", "next"],
            saved.GetProperty("slides").EnumerateArray().Select(slide =>
                slide.GetProperty("partner").GetString() is { } partner
                    ? $"{slide.GetProperty("kind").GetString()}:{partner}"
                    : slide.GetProperty("kind").GetString()));
        var sections = saved.GetProperty("body").GetProperty("sections");
        Assert.Equal(5, sections.GetArrayLength());
        Assert.Contains("Partner takes the manual pass out of the close.", LinesOf(saved, 1));
        Assert.Contains("Second takes the manual pass out of the close.", LinesOf(saved, 2));

        // The last slide is built by code: the booking words, the Brief's question, the site as text.
        var last = sections[4];
        Assert.Equal("Answer these questions when booking your free consultation", last.GetProperty("heading").GetString());
        Assert.Equal(["How many accounts are reconciled by hand each month?"], LinesOf(saved, 4));
        Assert.Equal(
            "geekatyourspot.com",
            last.GetProperty("paragraphs")[1].GetProperty("runs")[0].GetProperty("text").GetString());

        // The post is the caption, and it is not a section of the deck.
        Assert.Equal(
            "Most controllers lose the first week of the month to the close, and nobody budgets for it.\n\n"
            + "The slides walk through where the hours go and what takes them back.",
            saved.GetProperty("caption").GetString());
        Assert.DoesNotContain(GccLinkedInDocument.PostHeading, calls[^1].Text, StringComparison.Ordinal);

        // The links are for the first comment, never in the post: the booking link, then each
        // partner's tool page on the publisher's site.
        Assert.Equal(
            "Book your free consultation: https://geekatyourspot.com/#consultationAppointment2xl\n\n"
            + "Partner: https://geekatyourspot.com/tools/accounting/partner\n"
            + "Second: https://geekatyourspot.com/tools/accounting/second",
            saved.GetProperty("firstComment").GetString());
        Assert.Equal(
            ["#consultationAppointment2xl", "/tools/accounting/partner", "/tools/accounting/second"],
            saved.GetProperty("links").EnumerateArray().Select(link => link.GetProperty("href").GetString()));
        Assert.DoesNotContain("http", saved.GetProperty("caption").GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public async Task No_link_and_no_em_dash_is_inside_the_saved_deck_and_the_slides_continue_the_cover_as_it_is_saved()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(Brief);

        // The stand-in writer used the em dash in the cover's line, in a slide's line and in the post.
        Assert.DoesNotContain('—', calls[^1].Text);
        var body = Saved(calls).GetProperty("body").GetRawText();
        Assert.DoesNotContain("\"href\":\"", body, StringComparison.Ordinal);
        Assert.Contains("What a hand-run close costs, and what replaces it.", body, StringComparison.Ordinal);
        Assert.Contains("Reconciliations start late, every month.", body, StringComparison.Ordinal);

        // The slides request is shown the cover as the deck carries it, not as the writer returned it.
        var slides = calls.Single(call => call.Label == "slides").Text;
        Assert.Contains("Title: The close that runs on one spreadsheet", slides, StringComparison.Ordinal);
        Assert.Contains("Line: What a hand-run close costs, and what replaces it.", slides, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deck_is_told_neither_the_pages_four_phase_method_nor_a_length_band()
    {
        var brief = (JsonObject)JsonNode.Parse(Brief)!;
        brief["lengthBand"] = "long";

        var calls = await BriefFieldReachTests.WriteDeckAsync(brief.ToJsonString());

        foreach (var (label, text) in calls.Where(call => call.Label != "the saved page"))
        {
            Assert.False(text.Contains("ROLE & METHOD", StringComparison.Ordinal), $"{label} carries the page's method note.");
            Assert.False(text.Contains("Length band", StringComparison.Ordinal), $"{label} carries a length band.");
            // And none of the page's tools to link: a deck carries no link.
            Assert.False(text.Contains("/tools/", StringComparison.Ordinal), $"{label} carries a tool page's address.");
        }
    }

    // ---- the partners --------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "this project has 1 (Partner)")]
    [InlineData(9, "this project has 9 (")]
    public async Task A_project_with_too_few_or_too_many_partners_is_refused_before_a_request_is_sent(int partners, string says)
    {
        var urls = Enumerable.Range(0, partners).Select(i => i == 0 ? "https://partner.test/" : $"https://vendor{i}.test/").ToList();
        var asked = false;

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: (_, scripted) =>
            {
                asked = true;
                return scripted;
            },
            partnerUrls: urls));

        Assert.StartsWith("Refused: a PDF / LinkedIn document has one slide for each partner tool and takes 2 to 8 partners", refused.Message);
        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
        Assert.False(asked, "A request was sent for a deck that could not be written.");
    }

    [Fact]
    public async Task Eight_partners_are_a_deck_of_twelve_slides()
    {
        var urls = Enumerable.Range(0, 8).Select(i => i == 0 ? "https://partner.test/" : $"https://vendor{i}.test/").ToList();

        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(Brief, partnerUrls: urls));

        Assert.Equal(12, saved.GetProperty("slides").GetArrayLength());
        Assert.Equal(11, saved.GetProperty("body").GetProperty("sections").GetArrayLength());
        Assert.Equal(9, saved.GetProperty("links").GetArrayLength());
        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public void A_partner_with_no_passage_refuses_the_deck_by_name_as_it_refuses_a_pillar()
    {
        Assert.Equal(
            "Refused: the PDF / LinkedIn document must name every declared partner, and Second returned no passage for this "
            + "topic. Re-crawl that partner, or remove it from the project, then retry.",
            GccGenerationCoordinator.PartnerEvidenceRefusal("linkedin-document", ["Second"]));
        Assert.Null(GccGenerationCoordinator.PartnerEvidenceRefusal("PDF / LinkedIn document", []));
    }

    // ---- a number the research does not hold ----------------------------------------------------------

    [Fact]
    public async Task A_line_that_states_a_number_the_research_does_not_hold_is_deleted_and_the_deck_is_saved()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("slides", reply => Lines(reply, 1)[1] = "It cuts the close by 40 hours a month."));

        var saved = Saved(calls);
        Assert.Equal(["Partner takes the manual pass out of the close."], LinesOf(saved, 1));
        var warning = Assert.Single(Warnings(saved));
        Assert.StartsWith("The PDF / LinkedIn document: 1 sentence was deleted because it states a number", warning);
        Assert.Contains("\"It cuts the close by 40 hours a month.\" (40 hours), under \"What slide two settles\"", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sentence_of_the_post_that_states_such_a_number_is_deleted_and_the_rest_of_the_post_is_kept()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("cover and post", reply =>
                reply["post"] = "The close eats the first week of the month. Most teams lose 40 hours to it.\n\nThe slides show where it goes."));

        var saved = Saved(calls);
        Assert.Equal(
            "The close eats the first week of the month.\n\nThe slides show where it goes.",
            saved.GetProperty("caption").GetString());
        var warning = Assert.Single(Warnings(saved));
        Assert.Contains($"\"Most teams lose 40 hours to it.\" (40 hours), under \"{GccLinkedInDocument.PostHeading}\"", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_title_that_states_such_a_number_refuses_the_deck()
    {
        // A line can be taken out and the slide kept; a title cannot, and a slide needs one.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("slides", reply => Slide(reply, 3)["title"] = "Close the books 3 days faster")));

        Assert.StartsWith("Refused: the PDF / LinkedIn document.", refused.Message);
        Assert.Contains("3 days in \"Close the books 3 days faster\"", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_count_of_the_decks_own_slides_on_the_cover_is_not_a_number_to_look_up()
    {
        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("cover and post", reply =>
            {
                var cover = (JsonObject)reply["cover"]!;
                cover["title"] = "2 tools that take the close off one desk";
                cover["line"] = "The 2 tools, and where each one fits.";
            })));

        Assert.Equal("2 tools that take the close off one desk", saved.GetProperty("title").GetString());
        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public async Task Money_the_research_gives_in_another_currency_refuses_the_deck()
    {
        var research = GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    $"https://{BriefFieldReachTests.Host}/pricing", "Partner pricing", [],
                    ["Partner is billed at €40 a seat each month."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BriefFieldReachTests.WriteDeckAsync(
            Brief,
            research,
            reply: Changing("slides", reply => Lines(reply, 1)[1] = "Partner costs €40 a seat each month.")));

        Assert.StartsWith("Refused: the PDF / LinkedIn document.", refused.Message);
        Assert.Contains("Every amount is in US dollars.", refused.Message, StringComparison.Ordinal);
    }

    // ---- links, questions, partners -------------------------------------------------------------------

    [Theory]
    [InlineData("slides")]
    [InlineData("cover and post")]
    public async Task An_address_typed_on_a_slide_or_into_the_post_refuses_the_deck(string request)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing(request, reply =>
            {
                if (request == "slides") Lines(reply, 1)[1] = "Partner lists its plans at https://partner.test/pricing.";
                else reply["post"] = "The close eats a week.\n\nRead more at www.partner.test/close.";
            })));

        Assert.StartsWith("Refused: the PDF / LinkedIn document.", refused.Message);
        Assert.Contains("carries a link or a typed address", refused.Message, StringComparison.Ordinal);
        Assert.Contains(request == "slides" ? "https://partner.test/pricing" : "www.partner.test/close", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_partner_its_own_slide_does_not_name_is_listed_and_the_deck_is_saved()
    {
        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("slides", reply =>
            {
                Lines(reply, 2)[0] = "The second tool takes the manual pass out of the close.";
            })));

        Assert.Equal(
            ["The PDF / LinkedIn document does not name Second (slide 4, \"What slide three settles\") on its slide. The deck is saved as written."],
            Warnings(saved));
        Assert.Equal(5, saved.GetProperty("body").GetProperty("sections").GetArrayLength());
    }

    [Fact]
    public async Task A_title_that_names_its_partner_is_neither_refused_nor_listed()
    {
        // The request asks for the name in the lines. A deck is not lost, or marked, for naming its
        // own partner in a title as well, or instead.
        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("slides", reply =>
            {
                Slide(reply, 2)["title"] = "Second maps the vendor master";
                Lines(reply, 2)[0] = "The manual pass comes out of the close.";
            })));

        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public async Task A_slide_that_frames_questions_as_a_quiz_refuses_the_deck_and_a_post_that_asks_its_reader_does_not()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("slides", reply => Lines(reply, 0)[1] = "Ask yourself who signed off last month.")));
        Assert.Contains("frames questions as a quiz on a slide (\"ask yourself\")", refused.Message, StringComparison.Ordinal);

        // The post is a post in its own right, and a hook may put a question to its reader.
        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(
            Brief,
            reply: Changing("cover and post", reply =>
                reply["post"] = "Ask yourself who signed off the last close.\n\nThe slides show what changes.")));
        Assert.StartsWith("Ask yourself who signed off the last close.", saved.GetProperty("caption").GetString());
        Assert.Empty(Warnings(saved));
    }

    [Fact]
    public async Task A_post_longer_than_LinkedIn_takes_is_listed_and_the_deck_is_saved()
    {
        var longPost = string.Join("\n\n", Enumerable.Repeat("The close takes the controller away from everything else that needs a decision.", 45));
        Assert.InRange(longPost.Length, GccDraftGuard.LinkedInPostMaxCharacters + 1, int.MaxValue);

        var saved = Saved(await BriefFieldReachTests.WriteDeckAsync(
            Brief, reply: Changing("cover and post", reply => reply["post"] = longPost)));

        Assert.Equal(
            [$"The PDF / LinkedIn document's post is {longPost.Length:N0} characters. LinkedIn takes 3,000 in a post: shorten it before posting."],
            Warnings(saved));
        Assert.Null(GccDraftGuard.PostLength(new string('x', GccDraftGuard.LinkedInPostMaxCharacters)));
    }

    [Fact]
    public async Task A_partners_page_the_project_does_not_have_is_named_when_the_run_is_over_as_it_is_for_a_pillar()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(Brief);
        var create = BriefFieldReachTests.Create("linkedin-document", "AI implementation", Brief, null, BriefFieldReachTests.ProjectId);
        var partnerPages = GccPartnerToolPages.For(create, BriefFieldReachTests.DeckPartnerUrls, new CompanyProfileOptions().ToolBaseUrl);
        IReadOnlyList<GccGenerationCoordinator.GeneratedPiece> pieces =
            [new GccGenerationCoordinator.GeneratedPiece("linkedin-document", calls[^1].Text, "AI implementation")];

        // The project has Partner's tool page and not Second's, and this run was asked for no tool page.
        var named = GccGenerationCoordinator.NameMissingToolPages(pieces, partnerPages, ["Partner"], toolPagesAskedFor: false);

        Assert.Equal(
            ["Links to the Second tool page, and this project has no Second tool page: it has not been generated. "
             + "Generate it, or take the link out, before this is published."],
            Warnings(JsonDocument.Parse(Assert.Single(named).BodyJson).RootElement));

        // With both pages on the project the deck says nothing.
        var complete = GccGenerationCoordinator.NameMissingToolPages(pieces, partnerPages, ["Partner", "Second"], toolPagesAskedFor: false);
        Assert.Empty(Warnings(JsonDocument.Parse(Assert.Single(complete).BodyJson).RootElement));
    }

    // ---- what goes on the run's record and not in the operator's list ----------------------------------

    [Fact]
    public async Task A_slide_outside_its_numbers_is_on_the_runs_record_and_is_not_listed_for_the_operator()
    {
        var (calls, recorded) = await WriteRecordedAsync(reply: Changing("slides", reply =>
        {
            Slide(reply, 0)["title"] = "Where the first week of every month actually goes for a controller";
            var lines = Lines(reply, 3);
            lines.Add("A third line.");
            lines.Add("A fourth line.");
            lines.Add("A fifth line, which is one more than a slide is asked for.");
        }));

        // The PDF fits a slide's text to its page, so there is nothing here for the operator to act on.
        Assert.Empty(Warnings(Saved(calls)));

        var deck = Assert.Single(Events(recorded, "deck"));
        var slides = deck.GetProperty("slides").EnumerateArray().ToList();
        Assert.Equal(6, slides.Count);
        Assert.Equal(["cover", "problem", "partner", "partner", "approach", "next"], slides.Select(slide => slide.GetProperty("kind").GetString()));
        Assert.Equal(
            ["a title of 12 words (10 at most)"],
            slides[1].GetProperty("outsideItsNumbers").EnumerateArray().Select(over => over.GetString()));
        Assert.Equal(
            ["5 lines (4 at most)"],
            slides[4].GetProperty("outsideItsNumbers").EnumerateArray().Select(over => over.GetString()));
        Assert.Empty(slides[2].GetProperty("outsideItsNumbers").EnumerateArray());
        // The last slide is the operator's own words and is held to no number.
        Assert.Empty(slides[5].GetProperty("outsideItsNumbers").EnumerateArray());
        Assert.Equal(3, deck.GetProperty("links").GetArrayLength());

        // How each slide sets in the PDF is on the record: its sizes, and that none had to be set under
        // the legible size. The slide with five lines is set smaller than the slide with two.
        var pdf = deck.GetProperty("pdf");
        var set = pdf.GetProperty("slides").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6], set.Select(slide => slide.GetProperty("slide").GetInt32()));
        Assert.All(set, slide =>
        {
            Assert.False(slide.GetProperty("underLegibleSize").GetBoolean());
            Assert.True(slide.GetProperty("textSize").GetDouble() >= GccLinkedInDocumentPdfRenderer.LegibleTextSize);
        });
        Assert.True(set[4].GetProperty("textSize").GetDouble() <= set[2].GetProperty("textSize").GetDouble());
        Assert.Empty(pdf.GetProperty("charactersNotHeld").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, pdf.GetProperty("failure").ValueKind);

        // The checks' verdict is on the record too, with the post as the section after the last slide.
        var verdict = Assert.Single(Events(recorded, "verdict"));
        Assert.True(verdict.GetProperty("clean").GetBoolean());
        Assert.Equal(3, verdict.GetProperty("emDashesRemoved").GetInt32());
    }

    // ---- research: the one way every type receives it ------------------------------------------------

    [Fact]
    public async Task Nothing_a_search_brought_back_is_left_out_of_either_request_for_a_count()
    {
        var calls = await BriefFieldReachTests.WriteDeckAsync(
            Brief, BriefFieldReachTests.ResearchPastEveryOldCount(), competitors: BriefFieldReachTests.CompetitorCrawl);
        var cover = calls.Single(call => call.Label == "cover and post").Text;
        var slides = calls.Single(call => call.Label == "slides").Text;

        // Both requests: every passage of the partner's page and of its help page, and what the
        // publisher states on its own site.
        foreach (var text in new[] { cover, slides })
        {
            Assert.All(
                Enumerable.Range(1, BriefFieldReachTests.PassagesOnThePartnersPage),
                n => Assert.Contains($"- {BriefFieldReachTests.PartnerPassage(n)}", text, StringComparison.Ordinal));
            Assert.Contains("- Partner Widget help: setup takes one afternoon.", text, StringComparison.Ordinal);
            Assert.Contains("We map the close before we automate it.", text, StringComparison.Ordinal);
            // A competitor's address is never shown.
            Assert.DoesNotContain("contender", text, StringComparison.OrdinalIgnoreCase);
        }

        // The slides request, as a page's body: every competitor page with every passage and every
        // heading of its outline, and every one of the publisher's own pages on the topic.
        for (var c = 1; c <= BriefFieldReachTests.CompetitorPages; c++)
        {
            Assert.Contains($"[Competitor page {c}: ", slides, StringComparison.Ordinal);
            Assert.All(Enumerable.Range(1, 7), n => Assert.Contains($"- {BriefFieldReachTests.CompetitorPassage(c, n)}", slides, StringComparison.Ordinal));
            Assert.All(Enumerable.Range(1, 30), n => Assert.Contains($"- {BriefFieldReachTests.CompetitorHeading(c, n)} (h2)", slides, StringComparison.Ordinal));
        }

        for (var o = 1; o <= BriefFieldReachTests.OwnSitePages; o++)
        {
            Assert.All(Enumerable.Range(1, 7), n => Assert.Contains($"- {BriefFieldReachTests.OwnSitePassage(o, n)}", slides, StringComparison.Ordinal));
        }

        // The cover and the post are shown no competitor page, as a page's opening is not.
        Assert.DoesNotContain("[Competitor page 1", cover, StringComparison.Ordinal);
        // A slide carries no tag, so neither request is told how to tag a heading.
        Assert.DoesNotContain("\"competitor:<exact heading text>\"", slides, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_more_research_than_its_model_accepts_each_request_is_sent_what_its_record_says_was_chosen_for_it()
    {
        // About 1,500 tokens a passage and 130 of them: more than the smaller model's 128,000 with
        // nothing else in the request.
        var filler = string.Join(' ', Enumerable.Range(0, 480).Select(n => $"item{(n * 7919) % 10007}"));
        var passages = Enumerable.Range(0, 130).Select(i => $"Passage {i:000} of the partner's research. {filler}").ToList();
        var research = GccResearchFetchService.Serialize(new GccResearchDocument(SerpIndex: null, Quoteables: []) with
        {
            Quoteables =
            [
                .. passages.Select((passage, i) => new GccQuoteablePage(
                    $"https://{BriefFieldReachTests.Host}/research/{i}", "Partner", [], [passage],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk)),
            ],
        });

        var (calls, recorded) = await WriteRecordedAsync(research, model: "gpt-4o");

        var evidence = Events(recorded, "evidence");
        Assert.Equal([0, 1], evidence.Select(record => record.GetProperty("call").GetInt32()));
        foreach (var record in evidence)
        {
            Assert.False(record.GetProperty("whole").GetBoolean());
            var size = record.GetProperty("request");
            Assert.Equal(128_000, size.GetProperty("accepts").GetInt32());
            Assert.InRange(size.GetProperty("withEverything").GetInt32() + size.GetProperty("reply").GetInt32(), 128_001, int.MaxValue);
            Assert.InRange(size.GetProperty("withReply").GetInt32(), 1, 128_000);

            static int PassageOf(JsonElement listed) =>
                int.Parse(listed.GetProperty("url").GetString()!.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
            var chosen = record.GetProperty("chosen").EnumerateArray().Select(PassageOf).ToList();
            var leftOut = record.GetProperty("leftOut").EnumerateArray().Select(PassageOf).ToList();
            Assert.NotEmpty(chosen);
            Assert.NotEmpty(leftOut);
            Assert.Equal(130, chosen.Count + leftOut.Count);

            var text = calls.Single(call => call.Label == (record.GetProperty("call").GetInt32() == 0 ? "cover and post" : "slides")).Text;
            Assert.All(chosen, i => Assert.Contains(passages[i], text, StringComparison.Ordinal));
            Assert.All(leftOut, i => Assert.DoesNotContain(passages[i], text, StringComparison.Ordinal));
            Assert.Contains(GccGenerateService.ResearchIsAPartNote, text, StringComparison.Ordinal);
        }

        // The same research goes whole to the larger model, in both requests.
        var (wholeCalls, wholeRecorded) = await WriteRecordedAsync(research);
        Assert.All(Events(wholeRecorded, "evidence"), record => Assert.True(record.GetProperty("whole").GetBoolean()));
        Assert.All(
            wholeCalls.Where(call => call.Label != "the saved page"),
            call => Assert.All(passages, passage => Assert.Contains(passage, call.Text, StringComparison.Ordinal)));
    }

    /// <summary>
    /// What a deck's two requests come to, counted through the real writer before either is sent, on
    /// research of the shape the pages' requests were measured on (the 15:29 UTC run of 2026-10-12):
    /// about 364,000 characters of partner research over five partners, 40 competitor pages with
    /// outlines of 30 headings, 8 of the publisher's own pages. Made-up text, real counter.
    /// </summary>
    [Fact]
    public async Task The_size_of_each_request_is_counted_on_research_of_a_real_runs_shape()
    {
        var partners = new[] { "partner.test", "vendor1.test", "vendor2.test", "vendor3.test", "vendor4.test" };
        static string Words(int seed, int words) =>
            string.Join(' ', Enumerable.Range(0, words).Select(n => $"word{(seed * 31 + n * 7919) % 4001}"));
        var research = GccResearchFetchService.Serialize(new GccResearchDocument(SerpIndex: null, Quoteables: []) with
        {
            // 5 partners, 14 pages each, 8 passages a page of about 640 characters: about 360,000.
            Quoteables =
            [
                .. partners.SelectMany((host, p) => Enumerable.Range(0, 14).Select(page => new GccQuoteablePage(
                    $"https://{host}/page-{page:00}", $"Page {page:00}", [],
                    [.. Enumerable.Range(0, 8).Select(n => $"What the vendor's page says. {Words(p * 1000 + page * 10 + n, 70)}.")],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk))),
            ],
            CompetitorQuoteables =
            [
                .. Enumerable.Range(1, 40).Select(c => new GccQuoteablePage(
                    $"https://contender.test/page-{c:00}", $"Other vendor page {c:00}", [],
                    [.. Enumerable.Range(0, 8).Select(n => $"What another vendor's page says. {Words(90_000 + c * 10 + n, 70)}.")],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk)),
            ],
            SiteQuoteables =
            [
                .. Enumerable.Range(1, 8).Select(o => new GccQuoteablePage(
                    $"https://acme.test/published-{o:00}", $"Published {o:00}", [],
                    [.. Enumerable.Range(0, 8).Select(n => $"What our own page says. {Words(190_000 + o * 10 + n, 70)}.")],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk)),
            ],
        });
        var parsed = GccResearchFetchService.Deserialize(research)!;
        var partnerCharacters = parsed.Quoteables.Sum(page => page.Paragraphs.Sum(passage => passage.Length));
        Assert.InRange(partnerCharacters, 340_000, 390_000);

        GccCompetitorAnalysisResolver Crawl() => GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(GccCompetitorAnalysisResolverTests.Project("https://contender.test")),
            new GccCompetitorAnalysisResolverTests.FakePages(
            [
                .. Enumerable.Range(1, 40).Select(c => GccCompetitorAnalysisResolverTests.BlockPage(
                    $"https://contender.test/page-{c:00}",
                    [.. Enumerable.Range(1, 30).Select(n => GccCompetitorAnalysisResolverTests.Heading(2, $"Other vendor outline {c:00} heading {n:00}"))])),
            ]),
            new GccCompetitorAnalysisResolverTests.FakeRag(
                hosts: [new GeekAPI.Services.GeekCrawler.GeekCrawlerRagHostIndex("https://contender.test", "contender.test", true, Guid.NewGuid().ToString())]));

        var (_, recorded) = await WriteRecordedAsync(
            research, partnerUrls: [.. partners.Select(host => $"https://{host}/")], competitors: Crawl);

        var evidence = Events(recorded, "evidence");
        Assert.Equal(2, evidence.Count);
        var sizes = evidence.Select(record => record.GetProperty("request")).ToList();
        foreach (var (size, name) in sizes.Zip(["the cover and the post", "the slides"]))
        {
            output.WriteLine(
                $"{name}: {size.GetProperty("tokens").GetInt32():N0} tokens sent, "
                + $"{size.GetProperty("withReply").GetInt32():N0} with the reply's reservation, of "
                + $"{size.GetProperty("accepts").GetInt32():N0}");
        }

        output.WriteLine($"partner research: {partnerCharacters:N0} characters; a deck of {partners.Length + 4} slides");
        Assert.All(evidence, record => Assert.True(record.GetProperty("whole").GetBoolean()));
        // The slides read what a page's body reads, the cover what its opening reads: the slides'
        // request is the larger, and both are a fraction of what the model accepts.
        Assert.InRange(sizes[1].GetProperty("tokens").GetInt32(), sizes[0].GetProperty("tokens").GetInt32() + 1, 400_000);
    }

    // ---- the pieces code builds ------------------------------------------------------------------------

    private static CompanyProfileOptions Company() => new();

    [Fact]
    public void The_last_slide_is_the_booking_words_the_operators_questions_and_the_site_as_text()
    {
        var withQuestions = GccLinkedInDocument.NextSlide(Company(), ["  Who approves a journal entry today?  ", "", "How long does the close take?"])!;
        Assert.Equal("Answer these questions when booking your free consultation", withQuestions.Heading);
        var list = Assert.IsType<ListParagraph>(withQuestions.Paragraphs[0]);
        Assert.Equal(
            ["Who approves a journal entry today?", "How long does the close take?"],
            list.Items.Select(item => string.Concat(item.Select(run => run.Text))));
        var site = Assert.IsType<TextParagraph>(withQuestions.Paragraphs[1]);
        Assert.Equal("geekatyourspot.com", Assert.Single(site.Runs).Text);
        // No link anywhere on it: LinkedIn does not make one inside a document clickable.
        Assert.Null(withQuestions.Href);
        Assert.All(list.Items.SelectMany(item => item).Concat(site.Runs), run => Assert.Null(run.Href));

        var withNone = GccLinkedInDocument.NextSlide(Company(), [])!;
        Assert.Equal("Book your free consultation", withNone.Heading);
        Assert.Equal("geekatyourspot.com", Assert.Single(Assert.IsType<TextParagraph>(Assert.Single(withNone.Paragraphs)).Runs).Text);
    }

    [Fact]
    public void A_publisher_with_no_scheduler_or_no_wording_has_no_last_slide_and_the_deck_says_so()
    {
        Assert.Null(GccLinkedInDocument.NextSlide(new CompanyProfileOptions { ConsultationAnchorHref = " " }, ["A question?"]));
        Assert.Null(GccLinkedInDocument.NextSlide(new CompanyProfileOptions { ConsultationClosingLead = "" }, ["A question?"]));
        Assert.Null(GccLinkedInDocument.NextSlide(new CompanyProfileOptions { ConsultationClosingLinkWithoutQuestions = "" }, []));

        // A deck built without it is saved, and says what it is missing.
        IReadOnlyList<GccDeckSlidePlan> slides = [.. GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 0)];
        var verdict = GccDraftGuard.LinkedInDocument(Deck(slides, "The post."), Inputs(), slides);
        var finding = Assert.Single(verdict.Findings);
        Assert.Equal("deck-last-slide", finding.Check);
        Assert.False(finding.Refuses);
    }

    [Fact]
    public void The_links_are_the_booking_link_and_each_partners_page_and_none_without_a_site_to_put_them_on()
    {
        var pages = new[]
        {
            new GccPartnerToolPage("ramp.test", "Ramp", "ramp", "/tools/accounting/accounts-payable/ramp"),
            new GccPartnerToolPage("bill.test", "Bill", "bill", "tools/accounting/accounts-payable/bill"),
        };

        var links = GccLinkedInDocument.Links(Company(), pages);

        Assert.Equal(
            [
                new GccLinkedInDocument.CommentLink("Book your free consultation", "#consultationAppointment2xl", "https://geekatyourspot.com/#consultationAppointment2xl"),
                new GccLinkedInDocument.CommentLink("Ramp", "/tools/accounting/accounts-payable/ramp", "https://geekatyourspot.com/tools/accounting/accounts-payable/ramp"),
                new GccLinkedInDocument.CommentLink("Bill", "/tools/accounting/accounts-payable/bill", "https://geekatyourspot.com/tools/accounting/accounts-payable/bill"),
            ],
            links);
        Assert.Equal(
            "Book your free consultation: https://geekatyourspot.com/#consultationAppointment2xl\n\n"
            + "Ramp: https://geekatyourspot.com/tools/accounting/accounts-payable/ramp\n"
            + "Bill: https://geekatyourspot.com/tools/accounting/accounts-payable/bill",
            GccLinkedInDocument.FirstComment(links));

        // No scheduler: the partners' pages alone. No site address: no link can be made whole, so none.
        var noScheduler = GccLinkedInDocument.Links(new CompanyProfileOptions { ConsultationAnchorHref = "" }, pages);
        Assert.Equal(["Ramp", "Bill"], noScheduler.Select(link => link.Label));
        Assert.Equal(
            "Ramp: https://geekatyourspot.com/tools/accounting/accounts-payable/ramp\n"
            + "Bill: https://geekatyourspot.com/tools/accounting/accounts-payable/bill",
            GccLinkedInDocument.FirstComment(noScheduler));
        Assert.Empty(GccLinkedInDocument.Links(new CompanyProfileOptions { ArticleBaseUrl = "/use-cases" }, pages));
        Assert.Equal(string.Empty, GccLinkedInDocument.FirstComment([]));
    }

    [Fact]
    public void The_post_goes_through_the_checks_as_a_section_and_comes_back_out_as_text()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);
        var deck = Deck(slides, post: null);

        var together = GccLinkedInDocument.WithPost(deck, "A first paragraph.\r\n\r\nA second,\nwith a line break in it.\n\n\n\nA third.");
        Assert.Equal(deck.Sections.Count + 1, together.Sections.Count);
        var post = GccLinkedInDocument.PostOf(together, slides.Count)!;
        Assert.Equal(GccLinkedInDocument.PostHeading, post.Heading);
        Assert.Equal(3, post.Paragraphs.Count);

        var (back, text) = GccLinkedInDocument.SplitPost(together, slides.Count);
        Assert.Equal("A first paragraph.\n\nA second,\nwith a line break in it.\n\nA third.", text);
        Assert.Equal(deck.Sections.Select(section => section.Heading), back.Sections.Select(section => section.Heading));

        // A document that holds no post is given back as it is.
        var (same, none) = GccLinkedInDocument.SplitPost(deck, slides.Count);
        Assert.Same(deck, same);
        Assert.Equal(string.Empty, none);
    }

    // ---- the guard, on a deck built by hand ------------------------------------------------------------

    private static GccGuardInputs Inputs(string numberEvidence = "") => new(
        Provenance: null,
        RequiredTools: ["Ramp", "Bill"],
        ConsultationHref: "#consultationAppointment2xl",
        AllowedLinkUrls: new HashSet<string>(),
        PublisherHosts: new HashSet<string>(),
        NumberEvidence: numberEvidence);

    /// <summary>
    /// A deck for Ramp and Bill as the writer's replies make it, each partner named in its slide's
    /// lines, with the post after the last slide when one is given.
    /// </summary>
    private static ContentDocument Deck(
        IReadOnlyList<GccDeckSlidePlan> slides, string? post, Func<GccDeckSlidePlan, Section, Section>? change = null)
    {
        var sections = new List<Section>();
        foreach (var slide in slides.Skip(1))
        {
            var section = slide.Kind == GccDeckSlideKind.Next
                ? GccLinkedInDocument.NextSlide(Company(), ["If your answers differ, who decides?"])!
                : GccLinkedInDocument.SlideSection(new GccLinkedInDocument.SlideReply(
                    slide.Kind switch
                    {
                        GccDeckSlideKind.Problem => "Invoices wait on one signature",
                        GccDeckSlideKind.Partner => "The approval that reroutes itself",
                        _ => "From receipt to payment, in order",
                    },
                    slide.Partner is { } partner
                        ? [$"{partner} reassigns an invoice when its approver is away.", "Nothing waits on one inbox."]
                        : ["An approver goes on leave and nothing reroutes.", "The invoice is paid late."]));
            sections.Add(change?.Invoke(slide, section) ?? section);
        }

        var cover = GccLinkedInDocument.CoverSection(new GccLinkedInDocument.CoverReply(
            "Invoices that wait a week for a signature", "Where the week goes, and what takes it back."));
        var deck = new ContentDocument(change?.Invoke(slides[0], cover) ?? cover, sections);
        return post is null ? deck : GccLinkedInDocument.WithPost(deck, post);
    }

    [Fact]
    public void A_deck_that_names_each_partner_on_its_slide_and_ends_on_the_booking_slide_passes_every_check()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);

        // The last slide's question holds "if your answers", which is the operator's own wording and
        // not a quiz the writer framed.
        var verdict = GccDraftGuard.LinkedInDocument(Deck(slides, "A post.\n\nIts second paragraph."), Inputs(), slides);

        Assert.True(verdict.Clean, string.Join(" | ", verdict.Findings.Select(finding => finding.Detail)));
    }

    [Fact]
    public void A_partner_named_for_its_domain_is_not_read_as_an_address()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Bill.com", "Ramp"], lastSlides: 1);
        var deck = Deck(slides, "Bill.com and Ramp, side by side.");

        var verdict = GccDraftGuard.LinkedInDocument(deck, Inputs() with { RequiredTools = ["Bill.com", "Ramp"] }, slides);

        Assert.True(verdict.Clean, string.Join(" | ", verdict.Findings.Select(finding => finding.Detail)));
    }

    [Fact]
    public void A_link_set_on_any_part_of_a_deck_refuses_it()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);
        var deck = Deck(slides, "A post.", (slide, section) => slide.Kind == GccDeckSlideKind.Approach
            ? section with { Paragraphs = [new TextParagraph([new Run("See the method", Href: "/use-cases/method")])] }
            : section);

        var finding = Assert.Single(GccDraftGuard.LinkedInDocument(deck, Inputs(), slides).Findings);

        Assert.Equal("links", finding.Check);
        Assert.True(finding.Refuses);
        Assert.Contains("/use-cases/method", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quotation_in_a_deck_refuses_it()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);
        var deck = Deck(slides, "A post.", (slide, section) => slide.Kind == GccDeckSlideKind.Problem
            ? section with { Paragraphs = [.. section.Paragraphs, new QuoteParagraph([new Run("We closed in two days.")])] }
            : section);

        var finding = Assert.Single(GccDraftGuard.LinkedInDocument(deck, Inputs(), slides).Findings);

        Assert.Equal("no-quotation", finding.Check);
        Assert.True(finding.Refuses);
    }

    [Fact]
    public void A_title_and_the_covers_line_are_read_for_figures_and_one_the_evidence_holds_passes()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);
        ContentDocument WithTitle() => Deck(slides, "A post.", (slide, section) => slide.Kind == GccDeckSlideKind.Problem
            ? section with { Heading = "A late invoice costs $45 in fees" }
            : section);

        var unknown = Assert.Single(GccDraftGuard.LinkedInDocument(WithTitle(), Inputs(), slides).Findings);
        Assert.Equal("numbers", unknown.Check);
        Assert.True(unknown.Refuses);
        Assert.Contains("$45", unknown.Detail, StringComparison.Ordinal);

        Assert.True(GccDraftGuard.LinkedInDocument(WithTitle(), Inputs("The vendor charges a $45 late fee."), slides).Clean);
    }

    [Fact]
    public void What_each_slide_holds_is_counted_against_the_decks_numbers()
    {
        IReadOnlyList<GccDeckSlidePlan> slides = GccLinkedInDocument.Slides(["Ramp", "Bill"], lastSlides: 1);
        var deck = Deck(slides, post: null, (slide, section) => slide.Kind switch
        {
            GccDeckSlideKind.Cover => section with
            {
                Paragraphs = [new TextParagraph([new Run(string.Join(' ', Enumerable.Repeat("word", 21)))])],
            },
            GccDeckSlideKind.Approach => section with
            {
                Paragraphs = [new ListParagraph(false, [[new Run(string.Join(' ', Enumerable.Repeat("word", 15)))]])],
            },
            _ => section,
        });

        var sizes = JsonSerializer.SerializeToElement(GccDraftGuard.SlideSizes(deck, slides)).EnumerateArray().ToList();

        Assert.Equal(6, sizes.Count);
        Assert.Equal(["a line of 21 words (20 at most)"], sizes[0].GetProperty("outsideItsNumbers").EnumerateArray().Select(over => over.GetString()));
        Assert.Equal(
            ["1 line(s) (2 at least)", "a line of 15 words (14 at most)"],
            sizes[4].GetProperty("outsideItsNumbers").EnumerateArray().Select(over => over.GetString()));
        Assert.Equal("Ramp", sizes[2].GetProperty("partner").GetString());
        Assert.Equal(2, sizes[2].GetProperty("lines").GetInt32());
        Assert.Empty(sizes[5].GetProperty("outsideItsNumbers").EnumerateArray());
    }
}
