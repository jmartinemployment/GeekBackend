using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// "PDF / LinkedIn document", step 1 of <c>plans/enable-pdf.md</c>: what a deck is before a word of it
/// is written. Which slides it has for a Brief's partners, when it is refused, what each of its two
/// requests asks for, and the shape of the two replies.
/// </summary>
/// <remarks>
/// Nothing here writes a deck: the writer, its checks and the gate that still refuses the type are
/// the steps after this one. These tests hold the part a writer cannot change, the slide list, and
/// the part a reader of a run's record will look for first, what each request was told.
/// </remarks>
public class LinkedInDocumentPromptsTests
{
    private static readonly string[] FivePartnerSites =
    [
        "https://www.ramp.test/", "https://bill.test", "https://tipalti.test/ap",
        "https://melio.test", "https://airbase.test",
    ];

    private static readonly string[] FivePartners = ["Ramp", "Bill", "Tipalti", "Melio", "Airbase"];

    /// <summary>A Brief with the problem, a failure and the answer for every partner, and something
    /// entered under two of the five.</summary>
    private const string Brief = """
        {
          "nicheFraming": {
            "coreProblem": "Invoices wait a week for a signature nobody is chasing.",
            "painPoints": "An approver is on leave and nothing reroutes.\n\nDuplicate invoices are caught after they are paid.",
            "automationToPitch": "Routing that reassigns an invoice when its approver does not answer.",
            "perTool": {
              "ramp.test": {
                "coreProblem": "Card spend is coded after the month has closed.",
                "evidence": [
                  { "problem": "Receipts arrive weeks late.", "solution": "Receipt matching at the point of purchase.", "terms": ["receipt matching"] }
                ],
                "automationToPitch": "Spend coded as it happens."
              },
              "bill.test": {
                "evidence": [
                  { "problem": "An approver is on leave and nothing reroutes.", "solution": "Approval policies with a named backup approver.", "terms": ["approval policies"] }
                ]
              }
            }
          }
        }
        """;

    /// <summary>The same research with every failure under a partner and none entered for all of them.</summary>
    private const string BriefWithFailuresUnderPartnersOnly = """
        {
          "nicheFraming": {
            "coreProblem": "Invoices wait a week for a signature nobody is chasing.",
            "perTool": {
              "ramp.test": {
                "evidence": [
                  { "problem": "Receipts arrive weeks late.", "solution": "Receipt matching at the point of purchase.", "terms": ["receipt matching"] }
                ]
              }
            }
          }
        }
        """;

    private const string Research = "QUOTEABLE RESEARCH\nRamp: receipts are matched to the card transaction when it is made.";

    private static LinkedInDocumentPrompts Deck() => new(new GeekAPI.Services.Workflow.Services.PromptBuilders.ContentPromptBuilder());

    /// <summary>The context a deck's writer hands its prompts, built from a Brief the way the Pillar's is.</summary>
    private static ContentTypePromptContext ContextFor(string brief, IReadOnlyList<string> sites, string? evidence = Research)
    {
        var failuresUnderPartners = GccPartnerAnswers.CarryThePagesFailures(brief, sites);
        return new ContentTypePromptContext(
            GccOpeningAsksNothingTests.Context(),
            EvidenceBlock: evidence,
            NicheFraming: GccNicheFramingReader.ForEveryPartner(brief, failuresUnderPartners),
            Partners: GccRequiredToolMentions.For(brief, sites),
            PartnerAnswers: GccPartnerAnswers.Instruction(brief, sites));
    }

    private static string System(ChatCompletionRequest request) =>
        request.Messages.Single(message => message.Role == ChatRole.System).Content;

    private static string User(ChatCompletionRequest request) =>
        request.Messages.Single(message => message.Role == ChatRole.User).Content;

    private static string Assignment(ChatCompletionRequest request)
    {
        var user = User(request);
        return user[user.IndexOf("=== ASSIGNMENT ===", StringComparison.Ordinal)..];
    }

    private static int Count(string text, string phrase)
    {
        var count = 0;
        for (var at = text.IndexOf(phrase, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(phrase, at + phrase.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void A_brief_with_five_partners_gives_nine_slides_in_the_decks_order()
    {
        var partners = GccRequiredToolMentions.For(Brief, FivePartnerSites);
        Assert.Equal(FivePartners, partners);
        Assert.Null(GccLinkedInDocument.PartnerRefusal(partners));

        var slides = GccLinkedInDocument.Slides(partners, lastSlides: 1);

        Assert.Equal(
            [
                new GccDeckSlidePlan(GccDeckSlideKind.Cover),
                new GccDeckSlidePlan(GccDeckSlideKind.Problem),
                new GccDeckSlidePlan(GccDeckSlideKind.Partner, "Ramp"),
                new GccDeckSlidePlan(GccDeckSlideKind.Partner, "Bill"),
                new GccDeckSlidePlan(GccDeckSlideKind.Partner, "Tipalti"),
                new GccDeckSlidePlan(GccDeckSlideKind.Partner, "Melio"),
                new GccDeckSlidePlan(GccDeckSlideKind.Partner, "Airbase"),
                new GccDeckSlidePlan(GccDeckSlideKind.Approach),
                new GccDeckSlidePlan(GccDeckSlideKind.Next),
            ],
            slides);
        // One slide no request writes, and it is the last.
        Assert.Equal([slides[^1]], slides.Where(slide => slide.BuiltByCode));
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(5, 9)]
    [InlineData(8, 12)]
    public void A_deck_has_a_slide_for_each_partner_and_four_besides(int partners, int slides)
    {
        var names = Enumerable.Range(1, partners).Select(i => $"Tool{i}").ToList();

        Assert.Null(GccLinkedInDocument.PartnerRefusal(names));
        Assert.Equal(slides, GccLinkedInDocument.Slides(names, lastSlides: 1).Count);
        Assert.InRange(slides, ContentLengthTargets.LinkedInDocumentMinSlides, ContentLengthTargets.LinkedInDocumentMaxSlides);
    }

    [Fact]
    public void A_brief_with_one_partner_is_refused_saying_how_many_it_has()
    {
        var partners = GccRequiredToolMentions.For("{}", ["https://ramp.test"]);

        Assert.Equal(
            "Refused: a PDF / LinkedIn document has one slide for each partner tool and takes 2 to 8 partners; "
            + "this project has 1 (Ramp). Nothing was written for it.",
            GccLinkedInDocument.PartnerRefusal(partners));
    }

    [Fact]
    public void A_project_with_no_partner_or_with_nine_is_refused_the_same_way()
    {
        Assert.Equal(
            "Refused: a PDF / LinkedIn document has one slide for each partner tool and takes 2 to 8 partners; "
            + "this project has none. Nothing was written for it.",
            GccLinkedInDocument.PartnerRefusal([]));

        var nine = Enumerable.Range(1, 9).Select(i => $"Tool{i}").ToList();
        Assert.Equal(
            "Refused: a PDF / LinkedIn document has one slide for each partner tool and takes 2 to 8 partners; "
            + "this project has 9 (Tool1, Tool2, Tool3, Tool4, Tool5, Tool6, Tool7, Tool8, Tool9). Nothing was written for it.",
            GccLinkedInDocument.PartnerRefusal(nine));
    }

    [Fact]
    public void A_partner_named_twice_or_left_blank_is_one_partner_or_none()
    {
        // Two spellings of one partner are one slide, and a blank is no slide: a deck must not be
        // let through, or refused, on a count the project does not have.
        Assert.Equal(
            "Refused: a PDF / LinkedIn document has one slide for each partner tool and takes 2 to 8 partners; "
            + "this project has 1 (Ramp). Nothing was written for it.",
            GccLinkedInDocument.PartnerRefusal(["Ramp", " ramp ", ""]));
    }

    [Fact]
    public void The_slides_a_request_writes_are_the_problem_each_partner_and_the_approach()
    {
        var outline = Deck().OutlineFor(ContextFor(Brief, FivePartnerSites));

        Assert.Equal(7, outline.Count);
        Assert.All(outline, slot => Assert.True(slot.WritesItsOwnHeading));
        Assert.StartsWith("what goes wrong in this work today", outline[0].Covers);
        Assert.Equal(
            FivePartners.Select(partner => $"{partner}: the failure it answers and what is done about it"),
            outline.Skip(1).Take(5).Select(slot => slot.Covers));
        Assert.StartsWith("how the pieces fit", outline[6].Covers);
        // The cover is the opening's and the last slide is code's: neither is a slide a writer is assigned.
        Assert.DoesNotContain(outline, slot => slot.Covers!.Contains("cover", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(outline, slot => slot.Covers!.Contains("what to do next", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Each_slide_is_sent_to_what_the_operator_entered_for_it()
    {
        var outline = Deck().OutlineFor(ContextFor(Brief, FivePartnerSites));

        // The problem: the failures entered for every partner.
        Assert.Contains(GccNicheFraming.FramingHeading, outline[0].Guidance);
        Assert.Contains("It names no partner tool", outline[0].Guidance);
        // A partner: its own entries, under its name in the partners' block, and its own passages.
        Assert.Contains($"What the operator entered for Ramp is under its name in {GccPartnerAnswers.Heading}", outline[1].Guidance);
        Assert.Contains("only as Ramp's own passages in the research show it", outline[1].Guidance);
        Assert.Contains("Name Ramp in the lines, spelled exactly so, and never in the title.", outline[1].Guidance);
        // The approach: the operator's answer, and the publisher's own method where its site states one.
        Assert.Contains($"\"{GccNicheFraming.AnswerLabel}\" in {GccNicheFraming.FramingHeading}", outline[6].Guidance);
        Assert.Contains(GccPublisherPositions.DeckApproachSlotGuidance, outline[6].Guidance);
    }

    [Fact]
    public void A_slide_is_never_sent_to_a_block_its_request_does_not_carry()
    {
        // Nothing entered under any partner: no partners' block is built, and no slide names it.
        var bare = ContextFor("""{ "nicheFraming": { "coreProblem": "Invoices wait." } }""", FivePartnerSites);
        Assert.Null(bare.PartnerAnswers);

        var outline = Deck().OutlineFor(bare);

        Assert.DoesNotContain(outline, slot => (slot.Guidance ?? string.Empty).Contains(GccPartnerAnswers.Heading));
        Assert.Contains("what Ramp's own passages in the research show it doing", outline[1].Guidance);
        Assert.Contains("the passages in the research describe going wrong", outline[0].Guidance);
        Assert.DoesNotContain(GccPartnerAnswers.Heading, User(Deck().Body(bare)));
    }

    [Fact]
    public void With_every_failure_under_a_partner_the_problem_slide_is_built_on_the_partners_failures()
    {
        var context = ContextFor(BriefWithFailuresUnderPartnersOnly, FivePartnerSites);
        Assert.True(context.NicheFraming!.FailuresAreUnderThePartners);

        var problem = Deck().OutlineFor(context)[0];

        Assert.Contains($"\"{GccPartnerAnswers.FailureLabel}\" lines in {GccPartnerAnswers.Heading}", problem.Guidance);
        Assert.Contains("It names no failure the operator did not.", problem.Guidance);
        // And the block it is sent to is in the request, with the failure in it.
        var user = User(Deck().Body(context));
        Assert.Contains(GccPartnerAnswers.Heading, user);
        Assert.Contains("Receipts arrive weeks late.", user);
    }

    [Fact]
    public void The_cover_request_writes_the_cover_and_the_post_and_is_shown_every_slide()
    {
        var request = Deck().Lede(ContextFor(Brief, FivePartnerSites));
        var user = User(request);
        var assignment = Assignment(request);

        Assert.Contains("a deck of 9 slides attached to a LinkedIn post", user);
        Assert.Contains("THE COVER:", assignment);
        Assert.Contains("THE POST:", assignment);
        // Every slide, in order, for context: the cover first, each partner by name, the last marked as code's.
        var listed = FivePartners.Select(partner => assignment.IndexOf($"{partner}: the failure it answers", StringComparison.Ordinal)).ToList();
        Assert.All(listed, at => Assert.True(at > 0));
        Assert.Equal(listed.OrderBy(at => at), listed);
        Assert.Contains("1. the cover: the failure this deck is about", assignment);
        Assert.Contains("9. what to do next: the publisher's own booking words and questions (built by code, written by no request)", assignment);
        // A count on the cover is the deck's own.
        Assert.Contains("it has 5 slides on partner tools, one tool a slide. Say 5 or say no count", assignment);
        // The post, by Jeff's own post rule, as a second hook and never a note that a document is attached.
        Assert.Contains("150 to 300 words", assignment);
        Assert.Contains("It is a second hook, written as a post in its own right", assignment);
        Assert.Contains("It writes no address, no link and no hashtag", assignment);
        Assert.Contains("Channel mode (LinkedIn: Expert and Collaborative)", assignment);
    }

    [Fact]
    public void The_slides_request_assigns_every_written_slide_in_order_and_neither_the_cover_nor_the_last()
    {
        var context = ContextFor(Brief, FivePartnerSites) with
        {
            Lede = GccLinkedInDocument.CoverSection(new GccLinkedInDocument.CoverReply(
                "Invoices Wait On One Signature", "Five tools that stop an invoice waiting on a person.")),
        };

        var request = Deck().Body(context);
        var assignment = Assignment(request);

        Assert.Contains("Write these 7 slides, in this order.", assignment);
        Assert.Contains("1. Cover: what goes wrong in this work today", assignment);
        for (var i = 0; i < FivePartners.Length; i++)
        {
            Assert.Contains($"{i + 2}. Cover: {FivePartners[i]}: the failure it answers and what is done about it", assignment);
        }

        Assert.Contains("7. Cover: how the pieces fit, end to end, in the order they happen", assignment);
        Assert.DoesNotContain("8. Cover:", assignment);
        Assert.DoesNotContain("what to do next:", assignment);
        // The slide's numbers, said once, from the one place they are kept.
        Assert.Contains("a title of at most 10 words and 2 to 4 lines, each line at most 14 words", assignment);
        Assert.Contains("One idea a slide", assignment);
        // The cover already written is shown, so no slide repeats it.
        var user = User(request);
        Assert.Contains("THE COVER, ALREADY WRITTEN", user);
        Assert.Contains("Title: Invoices Wait On One Signature", user);
        Assert.Contains("Line: Five tools that stop an invoice waiting on a person.", user);
        Assert.Contains("the last slide, what to do next, is built from the publisher's own booking words and is not written here", user);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_requests_carry_what_the_operator_entered_and_the_research(bool cover)
    {
        var context = ContextFor(Brief, FivePartnerSites);
        var user = User(cover ? Deck().Lede(context) : Deck().Body(context));

        // The framing for every partner: the problem, both failures, the answer. Once.
        Assert.Equal(1, Count(user, GccNicheFraming.FramingHeading + " -- argue from it"));
        Assert.Contains("The problem: Invoices wait a week for a signature nobody is chasing.", user);
        Assert.Contains("1. An approver is on leave and nothing reroutes.", user);
        Assert.Contains("2. Duplicate invoices are caught after they are paid.", user);
        Assert.Contains("What answers it: Routing that reassigns an invoice when its approver does not answer.", user);
        Assert.Contains("A slide is a few short lines, so the deck does not list every one", user);
        // What was entered under each partner: its problem, its row with the remedy, its pitch. Once.
        Assert.Equal(1, Count(user, GccPartnerAnswers.Heading + " -- THE OPERATOR'S OWN RESEARCH"));
        Assert.Contains("The problem Ramp is for: Card spend is coded after the month has closed.", user);
        Assert.Contains("What Ramp does about it: Receipt matching at the point of purchase.", user);
        Assert.Contains("What the operator would pitch Ramp on: Spend coded as it happens.", user);
        Assert.Contains("What Bill does about it: Approval policies with a named backup approver.", user);
        // The research the writer handed it, before the operator's block.
        Assert.Contains(Research, user);
        Assert.True(user.IndexOf(Research, StringComparison.Ordinal) < user.IndexOf(GccPartnerAnswers.Heading, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_requests_print_the_brief_once_in_words_and_are_told_the_house_rules(bool cover)
    {
        var context = ContextFor(Brief, FivePartnerSites);
        var request = cover ? Deck().Lede(context) : Deck().Body(context);
        var user = User(request);
        var all = System(request) + "\n" + user;

        // The brief's choices are printed once, through the one class that turns a code into words,
        // under a heading that says what this request writes and not "the body" of a page.
        Assert.Equal(1, Count(user, "=== BRIEF CONTROLS"));
        Assert.Contains(
            cover ? "=== BRIEF CONTROLS (honor in the cover and in the post) ===" : "=== BRIEF CONTROLS (honor in every slide) ===",
            user);
        // The evidence is above the assignment, and the rule that points at it says so.
        Assert.Contains("appears only where the evidence in this message gives it", user);
        Assert.DoesNotContain("the evidence below gives it", user);
        // A deck is told its subject and told not to write to a keyword: it is not scored for search.
        Assert.Contains("The subject, as it is searched for: automated approval workflows. A deck is not written to a keyword", user);
        // No run of blank lines where a part had nothing to print.
        Assert.DoesNotContain("\n\n\n", user.Replace("\r\n", "\n"));
        Assert.DoesNotContain("commercial_investigation", all);
        Assert.DoesNotContain("problem_solution", all);
        Assert.DoesNotContain("consultant_professional", all);
        // The voice every page is written in, with the rule against the em dash in it.
        Assert.Contains(GccEmDash.Instruction, System(request));
        Assert.Contains("GROUNDING:", System(request));
        // A deck carries no link, no quotation and no question to the reader, and the writer is told
        // before the reply, since nothing is sent a second time.
        Assert.Contains("no address, no link, no hashtag, no emoji", user);
        Assert.Contains("Do not ask the reader questions", user);
        Assert.Contains("No customer and no case study is reported, named or unnamed.", user);
        Assert.EndsWith(
            "Answer in the JSON the output contract in the system message describes.",
            user.TrimEnd());
    }

    [Fact]
    public void Neither_request_tells_the_writer_to_link_a_tool()
    {
        // The pages' research brief has a block that says to link each tool on its first mention. A deck
        // carries no link at all, so its requests are built without that block.
        var context = ContextFor(Brief, FivePartnerSites);

        foreach (var user in new[] { User(Deck().Lede(context)), User(Deck().Body(context)) })
        {
            Assert.DoesNotContain("first mention", user, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/tools/accounting/accounts-payable/ramp", user);
        }
    }

    [Fact]
    public void Each_request_sends_its_own_strict_reply_format()
    {
        var context = ContextFor(Brief, FivePartnerSites);
        var cover = Deck().Lede(context);
        var slides = Deck().Body(context);

        Assert.Equal(GccLinkedInDocument.CoverAndPostSchemaName, cover.JsonSchemaName);
        Assert.Equal(GccLinkedInDocument.SlidesSchemaName, slides.JsonSchemaName);
        Assert.Contains("\"cover\": {\"title\": string", System(cover));
        Assert.Contains("\"post\": string", System(cover));
        Assert.Contains("{\"slides\": [{\"title\": string", System(slides));

        foreach (var schema in new[] { cover.JsonSchema!, slides.JsonSchema! })
        {
            using var parsed = JsonDocument.Parse(schema);
            AssertStrict(parsed.RootElement);
            // No field of either reply may be null: a slide with no title is not a slide.
            Assert.DoesNotContain("\"null\"", schema);
        }

        using var coverSchema = JsonDocument.Parse(cover.JsonSchema!);
        Assert.Equal(
            ["cover", "post"],
            coverSchema.RootElement.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["title", "line"],
            coverSchema.RootElement.GetProperty("properties").GetProperty("cover").GetProperty("properties")
                .EnumerateObject().Select(property => property.Name));
        using var slidesSchema = JsonDocument.Parse(slides.JsonSchema!);
        Assert.Equal(
            ["title", "lines"],
            slidesSchema.RootElement.GetProperty("properties").GetProperty("slides").GetProperty("items")
                .GetProperty("properties").EnumerateObject().Select(property => property.Name));
    }

    /// <summary>Every object in a reply format lists all of its fields as required and allows no other.</summary>
    private static void AssertStrict(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) AssertStrict(item);
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("properties", out var properties))
        {
            Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                properties.EnumerateObject().Select(property => property.Name),
                node.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        }

        foreach (var property in node.EnumerateObject()) AssertStrict(property.Value);
    }

    [Fact]
    public void The_replies_are_read_into_the_documents_own_parts()
    {
        var read = GccLinkedInDocument.ReadCoverAndPost(
            """{"cover":{"title":" Invoices Wait On One Signature ","line":"Five tools that stop an invoice waiting on a person."},"post":"Line one.\n\nLine two."}""");
        Assert.Equal("Line one.\n\nLine two.", read.Post);

        var opening = GccLinkedInDocument.CoverSection(read.Cover);
        Assert.Equal("Invoices Wait On One Signature", opening.Heading);
        var line = Assert.IsType<TextParagraph>(Assert.Single(opening.Paragraphs));
        Assert.Equal("Five tools that stop an invoice waiting on a person.", Assert.Single(line.Runs).Text);
        // And back, as the slides request is shown it.
        Assert.Equal(
            new GccLinkedInDocument.CoverReply("Invoices Wait On One Signature", "Five tools that stop an invoice waiting on a person."),
            GccLinkedInDocument.CoverOf(opening));
        Assert.Null(GccLinkedInDocument.CoverOf(null));

        var slides = GccLinkedInDocument.ReadSlides(
            """{"slides":[{"title":"Approvals Stall When One Person Is Out","lines":["Nothing reroutes while an approver is on leave.","", "Duplicates are caught after they are paid."]},{"title":"Coding Spend As It Happens","lines":["Ramp matches a receipt to its card charge at purchase."]}]}""",
            assigned: 2);
        var section = GccLinkedInDocument.SlideSection(slides[0]);
        Assert.Equal("h2", section.Tag);
        Assert.Equal("Approvals Stall When One Person Is Out", section.Heading);
        Assert.Empty(section.Children);
        var list = Assert.IsType<ListParagraph>(Assert.Single(section.Paragraphs));
        Assert.False(list.Ordered);
        // A blank line is no line.
        Assert.Equal(
            ["Nothing reroutes while an approver is on leave.", "Duplicates are caught after they are paid."],
            list.Items.Select(item => Assert.Single(item).Text));
    }

    [Theory]
    [InlineData("""{"cover":{"title":"","line":"A line."},"post":"A post."}""", "the cover carried no title")]
    [InlineData("""{"cover":{"title":"A title","line":" "},"post":"A post."}""", "the cover carried no line")]
    [InlineData("""{"cover":{"title":"A title","line":"A line."},"post":""}""", "the reply carried no post")]
    [InlineData("""{"post":"A post."}""", "the reply carried no cover")]
    public void A_cover_reply_with_a_part_missing_is_refused_with_the_reason(string reply, string reason)
    {
        var refused = Assert.ThrowsAny<Exception>(() => GccLinkedInDocument.ReadCoverAndPost(reply));
        Assert.Contains(reason, refused.Message);
    }

    [Theory]
    [InlineData("""{"slides":[{"title":"One","lines":["A line."]}]}""", "the reply carried 1 slide(s) and 2 were assigned")]
    [InlineData("""{"slides":[{"title":"One","lines":["A line."]},{"title":"Two","lines":["A line."]},{"title":"Three","lines":["A line."]}]}""", "the reply carried 3 slide(s) and 2 were assigned")]
    [InlineData("""{"slides":[{"title":"One","lines":["A line."]},{"title":" ","lines":["A line."]}]}""", "slide 2 carried no title")]
    [InlineData("""{"slides":[{"title":"One","lines":["", " "]},{"title":"Two","lines":["A line."]}]}""", "slide 1 carried no line")]
    [InlineData("""{"slides":null}""", "the reply carried no slides")]
    public void A_slides_reply_that_is_not_one_slide_for_each_assigned_is_refused_with_the_reason(string reply, string reason)
    {
        // Never fitted: a slide belongs to the partner its place gives it, so a reply one short cannot
        // say which partner each of its slides is about.
        var refused = Assert.ThrowsAny<Exception>(() => GccLinkedInDocument.ReadSlides(reply, assigned: 2));
        Assert.Contains(reason, refused.Message);
    }

    [Theory]
    [InlineData("linkedin-document")]
    [InlineData("linkedinDocument")]
    [InlineData("LinkedIn Document")]
    public void The_registry_finds_the_decks_prompts_by_the_types_key_however_it_is_written(string type)
    {
        Assert.IsType<LinkedInDocumentPrompts>(TestContentTypePrompts.Registry().Find(type));
    }

    [Fact]
    public void The_decks_own_wording_uses_no_em_dash()
    {
        // A writer copies the punctuation it is shown. The shared blocks these requests print are older
        // and are not this test's; the wording written for a deck is, and it holds none. Nor does what
        // the operator is told of a deck, or the last slide and the first comment code builds.
        var root = RepositoryRoot();
        foreach (var file in new[]
                 {
                     "GeekAPI/Services/ContentCreator/Writers/ContentPromptBuilder.LinkedInDocument.cs",
                     "GeekAPI/Services/ContentCreator/ContentTypes/LinkedInDocumentPrompts.cs",
                     "GeekAPI/Services/ContentCreator/GccLinkedInDocument.cs",
                     "GeekAPI/Services/ContentCreator/Writers/GccGenerateService.LinkedInDocument.cs",
                     "GeekAPI/Services/ContentCreator/Guardrail/GccDraftGuard.LinkedInDocument.cs",
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.False(text.Contains('—'), $"{file} holds an em dash.");
            Assert.False(text.Contains('–'), $"{file} holds an en dash.");
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GEEKBACKEND.slnx"))) return dir.FullName;
        }

        throw new InvalidOperationException("The repository root, the folder holding GEEKBACKEND.slnx, was not found above the test assembly.");
    }
}
