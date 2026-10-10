using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A page reaches its word floor by having enough sections (Jeff, 2026-10-10: "Add more sections").
/// </summary>
/// <remarks>
/// <para>
/// A body call stops on its own at about 650 words whatever it is asked for: the 21 body calls of the
/// 2026-10-07 run, none cut off. A pillar and a tool page each made three such calls, about 2,000 words
/// of body against a 3,000-word floor, and every batch was reported short of a floor no call reaches.
/// </para>
/// <para>
/// Ten body sections are five calls. What a section is asked for does not go down, because the same log
/// shows the writer giving less when asked for less. What a call is held to becomes its true share of
/// the page's floor, so the five calls' floors add up to the floor the page is scored against.
/// </para>
/// </remarks>
public sealed class MoreSectionsReachTheFloorTests
{
    private const int PageFloor = 3_000;

    private static ProjectGenerationContext Context() => GccOpeningAsksNothingTests.Context();

    private static IReadOnlyList<SectionSlot> PillarBody(GccNicheFraming? niche = null) =>
        [.. PillarPrompts.Outline(niche).Skip(1)];

    private static IReadOnlyList<SectionSlot> ToolBody() => ToolPrompts.Outline(Context(), "Partner Widget");

    private static IReadOnlyList<IReadOnlyList<SectionSlot>> Calls(IReadOnlyList<SectionSlot> body) =>
        [.. body.Chunk(GccGenerateService.SectionsPerBatch).Select(chunk => (IReadOnlyList<SectionSlot>)chunk)];

    public static TheoryData<string> BothTypes => new() { "pillar", "tool" };

    private static IReadOnlyList<SectionSlot> BodyOf(string type) => type == "pillar" ? PillarBody() : ToolBody();

    // ---- how many sections, and how many calls ----------------------------------------------------

    [Fact]
    public void A_pillar_is_its_opening_and_ten_body_sections()
    {
        Assert.Equal(11, PillarPrompts.Outline(null).Count);
        Assert.Equal(10, PillarPrompts.BodySectionCount);
        Assert.Equal(10, PillarBody().Count);
    }

    [Fact]
    public void A_tool_page_is_ten_body_sections_equal_to_the_pillar()
    {
        // "Tool ... at very least should equal a Pillar on every measure" (Jeff, 2026-09-28).
        Assert.Equal(PillarPrompts.BodySectionCount, ToolBody().Count);
    }

    [Theory]
    [MemberData(nameof(BothTypes))]
    public void The_body_is_written_in_five_calls(string type) => Assert.Equal(5, Calls(BodyOf(type)).Count);

    [Theory]
    [MemberData(nameof(BothTypes))]
    public void No_two_sections_cover_the_same_thing(string type)
    {
        var covers = BodyOf(type).Select(slot => slot.Covers).ToList();

        Assert.All(covers, cover => Assert.False(string.IsNullOrWhiteSpace(cover)));
        Assert.Equal(covers.Count, covers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- the operator's framing keeps its places ---------------------------------------------------

    [Fact]
    public void The_framing_guides_the_opening_the_two_failure_sections_the_approach_and_then_every_later_section()
    {
        var niche = new GccNicheFraming(
            "Approvals stall in inboxes", ["Invoices wait on one manager", "Nobody sees what is overdue"], "Route by amount and owner");
        Assert.True(niche.HasAny);

        var outline = PillarPrompts.Outline(niche);

        Assert.Equal(11, outline.Count);
        Assert.Equal(niche.ToGuidance(), outline[0].Guidance);
        Assert.Equal(niche.FailuresGuidance(), outline[1].Guidance);
        Assert.Equal(niche.FailuresGuidance(), outline[2].Guidance);
        Assert.StartsWith(niche.ApproachGuidance()!, outline[3].Guidance, StringComparison.Ordinal);
        Assert.EndsWith(GccPublisherPositions.ApproachSlotGuidance, outline[3].Guidance, StringComparison.Ordinal);
        Assert.All(outline.Skip(4), slot => Assert.Equal(niche.ApproachPointer(), slot.Guidance));
        Assert.Equal(7, outline.Skip(4).Count());
    }

    [Fact]
    public void A_brief_with_no_framing_guides_only_the_approach_section_from_the_publishers_own_method()
    {
        var outline = PillarPrompts.Outline(null);

        Assert.Equal(GccPublisherPositions.ApproachSlotGuidance, outline[3].Guidance);
        Assert.All(outline.Where((_, index) => index != 3), slot => Assert.Null(slot.Guidance));
    }

    // ---- the ask stays where it was ----------------------------------------------------------------

    [Fact]
    public void A_pillar_section_is_asked_for_what_it_was_asked_before()
    {
        Assert.All(PillarPrompts.Outline(null), slot => Assert.Equal("500-700 words", slot.Depth));
    }

    [Fact]
    public void No_tool_section_is_asked_for_less_than_a_section_was_asked_before()
    {
        // The sections that were not split keep their own figures; a split one is asked 500-700 for each part.
        var asks = ToolBody().Select(slot => slot.Depth).ToList();

        Assert.Equal("500-700 words", asks[0]);
        Assert.Equal("600-850 words", asks[1]);
        Assert.All(asks.Skip(2).Take(7), ask => Assert.Equal("500-700 words", ask));
        Assert.Equal("450-600 words", asks[9]);
    }

    // ---- what a call is held to --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothTypes))]
    public void Every_call_is_held_to_six_hundred_words_and_the_five_add_up_to_the_pages_floor(string type)
    {
        var (floor, _, _) = GccLongFormTypes.GetSeoLengthRules(type);
        Assert.Equal(PageFloor, floor);

        var floors = Calls(BodyOf(type)).Select(GccGenerateService.BatchFloorWords).ToList();

        Assert.Equal([600, 600, 600, 600, 600], floors);
        Assert.Equal(floor, floors.Sum());
    }

    [Theory]
    [MemberData(nameof(BothTypes))]
    public void No_call_owes_more_than_a_call_writes(string type)
    {
        // The measured yield is what a page's length is made of. A floor above it is reported short on every
        // page however the call is worded, which is the state this replaced: the page needs more sections.
        Assert.All(
            Calls(BodyOf(type)),
            call => Assert.True(
                GccGenerateService.BatchFloorWords(call) <= GccGenerateService.MeasuredWordsPerBodyCall,
                $"A {type} call owes {GccGenerateService.BatchFloorWords(call)} words and a call writes about "
                + $"{GccGenerateService.MeasuredWordsPerBodyCall}. Add sections; do not raise what a call owes."));
    }

    public static TheoryData<int, int> SectionCountsAndFloors
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var sections in Enumerable.Range(5, 7))
            {
                foreach (var floor in new[] { 1_800, 3_000, 3_250, 3_001 })
                {
                    data.Add(sections, floor);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SectionCountsAndFloors))]
    public void The_floor_is_divided_exactly_whatever_the_section_count(int sections, int floor)
    {
        var slots = Enumerable.Range(0, sections).Select(i => SectionSlot.Cover($"section {i}", "500-700 words")).ToList();

        var owed = SectionSlot.WithOwedWords(slots, floor);

        // Nothing lost to rounding, and nothing piled on the last section.
        Assert.Equal(floor, owed.Sum(slot => slot.OwedWords!.Value));
        Assert.True(owed.Max(slot => slot.OwedWords!.Value) - owed.Min(slot => slot.OwedWords!.Value) <= 1);

        // Calls that write the same number of sections owe the same, to within a word; an odd last call owes less.
        var calls = Calls(owed);
        Assert.Equal(floor, calls.Sum(GccGenerateService.BatchFloorWords));
        var fullCalls = calls.Where(call => call.Count == GccGenerateService.SectionsPerBatch)
            .Select(GccGenerateService.BatchFloorWords).ToList();
        Assert.True(fullCalls.Max() - fullCalls.Min() <= 1);
    }

    [Fact]
    public void What_a_section_is_asked_for_is_untouched_by_what_it_owes()
    {
        var slot = SectionSlot.Cover("one", "500-700 words", "guidance");

        var owed = Assert.Single(SectionSlot.WithOwedWords([slot], 300));

        Assert.Equal(slot.Depth, owed.Depth);
        Assert.Equal(slot.Covers, owed.Covers);
        Assert.Equal(slot.Guidance, owed.Guidance);
        Assert.Equal(300, owed.OwedWords);
    }

    [Fact]
    public void A_blog_batch_is_held_to_what_it_was_held_to_before()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder());
        var outline = blog.OutlineFor(new ContentTypePromptContext(
            Context(), BlogMetadata: new BlogMetadataDraft("Title", "Meta", ["ai"], ["a", "b"])));

        Assert.All(outline, slot => Assert.Null(slot.OwedWords));
        Assert.Equal(
            2 * ContentLengthTargets.BlogSectionMinWords,
            GccGenerateService.BatchFloorWords([.. outline.Take(2)]));
    }

    // ---- what the writer is told ---------------------------------------------------------------------

    private static string ToolCall(int batchIndex, IReadOnlyList<Section>? writtenSoFar = null)
    {
        var outline = ToolBody();
        var request = new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", null, "https://partner.test/widget"),
            "partner-widget",
            outline: [.. outline.Skip(batchIndex * 2).Take(2)],
            fullOutline: outline,
            batchIndex: batchIndex,
            writtenSoFar: writtenSoFar);
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    [Fact]
    public void A_tool_call_is_told_its_floor_once_as_its_share_of_the_page_and_its_ranges_as_what_to_aim_for()
    {
        var prompt = ToolCall(batchIndex: 1);

        Assert.Contains("Length: at least 600 words across the 2 sections above", prompt, StringComparison.Ordinal);
        Assert.Contains("this call's share of the page's floor", prompt, StringComparison.Ordinal);
        Assert.Contains("500-700 words. The range sizes this section against the others.", prompt, StringComparison.Ordinal);
        // No section's lower figure is called owed any more: the call's floor is the share above, not their sum.
        Assert.DoesNotContain("The lower figure is owed", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("the sum of each section's own lower figure", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_is_still_asked_for_twelve_hundred_words_though_it_is_two_of_ten_sections()
    {
        // The ask is what moves the writer; the share of the floor is only what the call is checked against.
        var prompt = ToolCall(batchIndex: 1);

        Assert.Contains("You are writing 2 of its 10 sections. Aim for about 1,200 words here", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("so your share is", prompt, StringComparison.Ordinal);
    }

    // ---- what the page already says ------------------------------------------------------------------

    private static Section Written(string heading, params string[] paragraphs) =>
        new("h2", heading, [.. paragraphs.Select(p => (Paragraph)new TextParagraph([new Run(p)]))], null, []);

    [Fact]
    public void A_later_call_is_shown_each_earlier_sections_heading_its_first_sentence_and_its_figures()
    {
        var block = ContentPromptBuilder.BuildWrittenSoFarBlock(
        [
            Written("Where the hours go", "Invoices are keyed twice before anyone approves them. That costs the team 40 hours a month.", "A second paragraph that is not shown."),
            Written("What a late payment costs", "A vendor paid late charges 2% and remembers it."),
        ])!;

        Assert.StartsWith("=== ALREADY WRITTEN ON THIS PAGE", block, StringComparison.Ordinal);
        Assert.Contains("- Where the hours go -- Invoices are keyed twice before anyone approves them.", block, StringComparison.Ordinal);
        Assert.Contains("- What a late payment costs -- A vendor paid late charges 2% and remembers it.", block, StringComparison.Ordinal);
        Assert.Contains("Figures cited:", block, StringComparison.Ordinal);
        Assert.Contains("40 hours", block, StringComparison.Ordinal);
        Assert.Contains("2%", block, StringComparison.Ordinal);
        Assert.Contains("Do not restate a point, an example or a figure from this list", block, StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_one_line_a_section_and_never_the_earlier_text_or_any_json()
    {
        var long1 = string.Join(" ", Enumerable.Repeat("This sentence stands in for a long paragraph of body text.", 60));
        var sections = Enumerable.Range(1, 8).Select(i => Written($"Section {i}", $"Section {i} opens here. {long1}")).ToList();

        var block = ContentPromptBuilder.BuildWrittenSoFarBlock(sections)!;

        Assert.DoesNotContain("stands in for a long paragraph", block, StringComparison.Ordinal);
        Assert.DoesNotContain("{", block, StringComparison.Ordinal);
        Assert.DoesNotContain("\"runs\"", block, StringComparison.Ordinal);
        Assert.Equal(8, block.Split('\n').Count(line => line.StartsWith("- Section ", StringComparison.Ordinal)));
        // Eight earlier sections, as the fifth call of a ten-section page sees them: a few hundred words.
        Assert.True(block.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 400);
    }

    [Fact]
    public void A_section_that_opens_on_a_subheading_is_shown_by_its_first_subsections_opening()
    {
        var child = new Section("h3", "The first step", [new TextParagraph([new Run("The invoice arrives by email. Then it waits.")])], null, []);
        var parent = new Section("h2", "How it runs", [], null, [child]);

        var block = ContentPromptBuilder.BuildWrittenSoFarBlock([parent])!;

        Assert.Contains("- How it runs -- The invoice arrives by email.", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_call_has_nothing_before_it_and_is_shown_no_such_block()
    {
        Assert.Null(ContentPromptBuilder.BuildWrittenSoFarBlock(null));
        Assert.Null(ContentPromptBuilder.BuildWrittenSoFarBlock([]));
        Assert.DoesNotContain("ALREADY WRITTEN ON THIS PAGE", ToolCall(batchIndex: 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_tool_call_carries_the_block_beside_the_opening_it_already_has()
    {
        var prompt = ToolCall(batchIndex: 1, writtenSoFar: [Written("What it removes", "Partner Widget removes the second keying.")]);

        Assert.Contains("=== ALREADY WRITTEN ON THIS PAGE", prompt, StringComparison.Ordinal);
        Assert.Contains("- What it removes -- Partner Widget removes the second keying.", prompt, StringComparison.Ordinal);
    }
}
