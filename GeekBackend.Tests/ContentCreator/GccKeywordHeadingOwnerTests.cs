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
/// automated owns it. A type that names no owner keeps the first batch.
/// </summary>
public sealed class GccKeywordHeadingOwnerTests
{
    private static readonly BlogMetadataDraft Meta = new("Title", "Meta", ["ai"], ["a", "b"]);
    private const string KeywordHeadings = "HEADINGS: at least one H2 contains the exact phrase";
    private const string AnotherCallWritesIt = "the page's keyword-bearing H2 is written by another call";

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
        Assert.DoesNotContain(KeywordHeadings, first, StringComparison.Ordinal);
        Assert.Contains(KeywordHeadings, second, StringComparison.Ordinal);
        Assert.DoesNotContain(AnotherCallWritesIt, second, StringComparison.Ordinal);
        Assert.Contains(AnotherCallWritesIt, third, StringComparison.Ordinal);
    }

    // ---- the pillar and the tool page are as they were -------------------------------------------

    [Fact]
    public void The_pillar_and_the_tool_page_still_ask_the_first_batch()
    {
        var pillar = new PillarPrompts(new ContentPromptBuilder());
        var pillarOutline = pillar.OutlineFor(new ContentTypePromptContext(GccOpeningAsksNothingTests.Context()));

        Assert.DoesNotContain(pillarOutline, slot => slot.OwnsKeywordHeading);
        Assert.True(SectionSlot.BatchOwnsKeywordHeading([.. pillarOutline.Skip(1).Take(2)], pillarOutline, 0));
        Assert.False(SectionSlot.BatchOwnsKeywordHeading([.. pillarOutline.Skip(3).Take(2)], pillarOutline, 1));
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
