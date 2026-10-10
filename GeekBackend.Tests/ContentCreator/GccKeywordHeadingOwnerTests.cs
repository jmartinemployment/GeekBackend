using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The page's keyword heading belongs to the section that can carry it honestly. It was the first batch by position:
/// the Blog's first two sections are about doing the work by hand, so asking one of them for a heading with
/// "Automated Approval Workflows" in it was asking for a contradiction, and the writer wrote "...Manual Approval
/// Workflows" and was reported as having no keyword heading (2026-10-07). The Blog's section on the work once it is
/// automated owns it. The Pillar and the Tool page name theirs since 2026-10-10, and the call that holds the
/// section is told which of its sections takes the phrase. An outline that names no owner keeps the first batch.
/// </summary>
public sealed class GccKeywordHeadingOwnerTests
{
    private static readonly BlogMetadataDraft Meta = new("Title", "Meta", ["ai"], ["a", "b"]);
    private const string OneOfYours = "HEADINGS: the page's keyword-bearing H2 is one of yours: the section that covers";
    private const string AtLeastOne = "HEADINGS: at least one H2 contains the exact phrase";
    private const string AnotherCallWritesIt = "the page's keyword-bearing H2 is written by another call";
    private static readonly ArticleMetadataDraft ArticleMeta = new("Title", "Meta", ["ai"], []);

    private static ContentTypePromptContext Ctx(IReadOnlyList<SectionSlot>? batch = null, int index = 0) =>
        new(GccOpeningAsksNothingTests.Context(), BlogMetadata: Meta, SectionBatch: batch, SectionBatchIndex: index);

    private static string BodyPrompt(BlogPrompts blog, IReadOnlyList<SectionSlot> outline, int batchIndex)
    {
        var batch = outline.Skip(batchIndex * 2).Take(2).ToList();
        var request = blog.Body(Ctx(batch, batchIndex));
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    // ---- the definition ---------------------------------------------------------------------------

    [Fact]
    public void An_outline_that_names_an_owner_asks_only_the_batch_that_holds_it()
    {
        SectionSlot[] outline =
        [
            SectionSlot.Cover("the problem"),
            SectionSlot.Cover("the cost"),
            SectionSlot.Cover("the mechanics") with { OwnsKeywordHeading = true },
            SectionSlot.Cover("the rollout"),
        ];

        Assert.False(SectionSlot.BatchOwnsKeywordHeading([.. outline.Take(2)], outline, 0));
        Assert.True(SectionSlot.BatchOwnsKeywordHeading([.. outline.Skip(2)], outline, 1));
    }

    [Fact]
    public void An_outline_that_names_no_owner_keeps_the_first_batch()
    {
        SectionSlot[] outline = [SectionSlot.Cover("a"), SectionSlot.Cover("b"), SectionSlot.Cover("c"), SectionSlot.Cover("d")];

        Assert.True(SectionSlot.BatchOwnsKeywordHeading([.. outline.Take(2)], outline, 0));
        Assert.False(SectionSlot.BatchOwnsKeywordHeading([.. outline.Skip(2)], outline, 1));
        Assert.True(SectionSlot.BatchOwnsKeywordHeading([.. outline.Take(2)], null, 0));
        Assert.False(SectionSlot.BatchOwnsKeywordHeading([.. outline.Skip(2)], null, 1));
    }

    // ---- the blog ---------------------------------------------------------------------------------

    [Fact]
    public void The_blogs_section_on_the_work_once_it_is_automated_owns_the_keyword_heading_and_nothing_else_does()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder());
        var outline = blog.OutlineFor(Ctx());

        var owner = Assert.Single(outline, slot => slot.OwnsKeywordHeading);
        Assert.Contains("is automated", owner.Covers, StringComparison.Ordinal);
        Assert.Equal(2, outline.ToList().IndexOf(owner));
    }

    [Fact]
    public void The_blog_asks_for_the_keyword_heading_in_the_batch_that_holds_that_section_and_says_so_to_the_others()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder());
        var outline = blog.OutlineFor(Ctx());

        var first = BodyPrompt(blog, outline, 0);
        var second = BodyPrompt(blog, outline, 1);
        var third = BodyPrompt(blog, outline, 2);

        Assert.Contains(AnotherCallWritesIt, first, StringComparison.Ordinal);
        Assert.DoesNotContain(OneOfYours, first, StringComparison.Ordinal);
        Assert.Contains(OneOfYours + " \"" + outline[2].Covers + "\".", second, StringComparison.Ordinal);
        Assert.DoesNotContain(AnotherCallWritesIt, second, StringComparison.Ordinal);
        Assert.Contains(AnotherCallWritesIt, third, StringComparison.Ordinal);
        Assert.All(new[] { first, second, third }, prompt => Assert.DoesNotContain(AtLeastOne, prompt, StringComparison.Ordinal));
    }

    // ---- the pillar -------------------------------------------------------------------------------

    private static string PillarBodyPrompt(PillarPrompts pillar, IReadOnlyList<SectionSlot> outline, int batchIndex)
    {
        var batch = outline.Skip(1 + batchIndex * 2).Take(2).ToList();
        var request = pillar.Body(new ContentTypePromptContext(
            GccOpeningAsksNothingTests.Context(), Metadata: ArticleMeta, SectionBatch: batch, SectionBatchIndex: batchIndex));
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    [Fact]
    public void The_pillars_section_on_how_the_approach_works_owns_the_keyword_heading_and_nothing_else_does()
    {
        var pillar = new PillarPrompts(new ContentPromptBuilder());
        var outline = pillar.OutlineFor(new ContentTypePromptContext(GccOpeningAsksNothingTests.Context()));

        var owner = Assert.Single(outline, slot => slot.OwnsKeywordHeading);
        Assert.StartsWith("how the approach works end to end", owner.Covers, StringComparison.Ordinal);
        // The opening, then the two sections on the work as it is done today, then it. A brief's framing
        // changes each section's guidance and not which one owns the heading.
        Assert.Equal(3, outline.ToList().IndexOf(owner));
        var framed = PillarPrompts.Outline(new GccNicheFraming("Approvals stall", ["Invoices wait"], "Route by amount"));
        Assert.Equal(3, framed.ToList().IndexOf(Assert.Single(framed, slot => slot.OwnsKeywordHeading)));
    }

    [Fact]
    public void The_pillars_first_call_is_two_sections_on_the_work_by_hand_and_is_not_asked_for_the_keyword_heading()
    {
        // The pillar of 2026-10-10 was reported for exactly this call writing no keyword heading.
        var pillar = new PillarPrompts(new ContentPromptBuilder());
        var outline = pillar.OutlineFor(new ContentTypePromptContext(GccOpeningAsksNothingTests.Context()));
        var body = outline.Skip(1).ToList();

        Assert.False(SectionSlot.BatchOwnsKeywordHeading([.. body.Take(2)], body, 0));
        Assert.True(SectionSlot.BatchOwnsKeywordHeading([.. body.Skip(2).Take(2)], body, 1));

        var first = PillarBodyPrompt(pillar, outline, 0);
        var second = PillarBodyPrompt(pillar, outline, 1);
        var last = PillarBodyPrompt(pillar, outline, 4);

        Assert.Contains(AnotherCallWritesIt, first, StringComparison.Ordinal);
        Assert.Contains(OneOfYours + " \"" + outline[3].Covers + "\".", second, StringComparison.Ordinal);
        Assert.Contains("None of your other headings needs", second, StringComparison.Ordinal);
        Assert.Contains(AnotherCallWritesIt, last, StringComparison.Ordinal);
    }

    // ---- the tool page ----------------------------------------------------------------------------

    private static string ToolBodyPrompt(IReadOnlyList<SectionSlot> outline, int batchIndex)
    {
        var request = new ContentPromptBuilder().BuildToolBodyPrompt(
            GccOpeningAsksNothingTests.Context(),
            ArticleMeta,
            new GeekAPI.Services.Workflow.Services.SchemaBuilders.SoftwareApplicationDescriptor("Partner Widget", null, "https://partner.test/widget"),
            "partner-widget",
            outline: [.. outline.Skip(batchIndex * 2).Take(2)],
            fullOutline: outline,
            batchIndex: batchIndex);
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    [Fact]
    public void The_tool_pages_section_on_what_the_product_does_owns_the_keyword_heading_and_its_call_is_told_which_section()
    {
        // Every tool page of 2026-10-10 has "The Challenges/Cost of Manual Automated Accounts Receivable":
        // the first call was told "at least one H2 contains the exact phrase", and its first section is the
        // problem as the reader has it today. The call is the same one; it is now told which section.
        var outline = ToolPrompts.Outline(GccOpeningAsksNothingTests.Context(), "Partner Widget");

        var owner = Assert.Single(outline, slot => slot.OwnsKeywordHeading);
        Assert.StartsWith("what Partner Widget actually does", owner.Covers, StringComparison.Ordinal);
        Assert.Equal(1, outline.ToList().IndexOf(owner));

        var first = ToolBodyPrompt(outline, 0);
        Assert.Contains(OneOfYours + " \"" + owner.Covers + "\".", first, StringComparison.Ordinal);
        Assert.Contains("None of your other headings needs", first, StringComparison.Ordinal);
        Assert.DoesNotContain(AtLeastOne, first, StringComparison.Ordinal);
        Assert.All(
            Enumerable.Range(1, 4).Select(index => ToolBodyPrompt(outline, index)),
            prompt => Assert.Contains(AnotherCallWritesIt, prompt, StringComparison.Ordinal));
    }

    [Fact]
    public void An_outline_that_names_no_owner_is_told_at_least_one_heading_in_its_first_call()
    {
        // A planned outline, as the Workflow product's article path hands over: headings a planning call
        // wrote, none flagged. The first call is asked, in the words it always was.
        var planned = new[] { "Where the hours go", "What changes first", "Mapping the data", "What to do next" }
            .Select(SectionSlot.Assigned).ToList();
        string Prompt(int index) => string.Join("\n", new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            GccOpeningAsksNothingTests.Context(), ArticleMeta,
            slots: [.. planned.Skip(index * 2).Take(2)], fullOutline: planned, isRegeneration: false, batchIndex: index)
            .Messages.Select(m => m.Content));

        Assert.Null(SectionSlot.KeywordHeadingOwnerIn(planned));
        Assert.Contains(AtLeastOne, Prompt(0), StringComparison.Ordinal);
        Assert.Contains(AnotherCallWritesIt, Prompt(1), StringComparison.Ordinal);
    }

    // ---- the blog links its tools the way the pillar does -----------------------------------------

    [Fact]
    public void The_blogs_approach_slot_ends_with_the_pillars_guidance_word_for_word_and_still_names_the_tools()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder());
        var blogGuidance = blog.OutlineFor(Ctx())[2].Guidance!;
        // The pillar's "how the approach works" slot: the opening, then the two "what is going wrong" slots, then it.
        var pillarGuidance = PillarPrompts.Outline(null)[3].Guidance!;

        Assert.EndsWith(pillarGuidance, blogGuidance, StringComparison.Ordinal);
        // The obligation to name them stays (a ban alone gave a page with no tools); only the link sentence goes.
        Assert.Contains("Name the partner tools that do this part of the work, in the prose", blogGuidance, StringComparison.Ordinal);
        Assert.DoesNotContain("Link the first substantive mention", blogGuidance, StringComparison.Ordinal);
    }

    [Fact]
    public void No_blog_or_pillar_slot_tells_the_writer_to_link_a_tool()
    {
        // A tool's page is put on its name by code (GccToolLinker); no slot, and no prompt, tells the writer
        // to link one. The blog's own "Link the first substantive mention" was the last slot that did.
        var blog = new BlogPrompts(new ContentPromptBuilder());

        var slots = blog.OutlineFor(Ctx()).Concat(PillarPrompts.Outline(null));

        Assert.All(slots, slot => Assert.DoesNotContain("link", slot.Guidance ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_blogs_body_prompt_still_carries_the_tools_block_that_names_each_tool()
    {
        var blog = new BlogPrompts(new ContentPromptBuilder());
        var outline = blog.OutlineFor(Ctx());
        var context = GccOpeningAsksNothingTests.Context();

        var request = blog.Body(new ContentTypePromptContext(
            context, BlogMetadata: Meta, SectionBatch: [.. outline.Skip(2).Take(2)], SectionBatchIndex: 1));
        var prompt = string.Join("\n", request.Messages.Select(m => m.Content));

        Assert.Contains("KNOWN TOOLS", prompt, StringComparison.Ordinal);
        Assert.Contains("\n- Ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/accounting/accounts-payable/ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("T# id", prompt, StringComparison.Ordinal);
    }
}
