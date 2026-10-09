using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The closing call to action had an ask and no destination, so the writer sent the reader off the
/// page -- to a contact page, a URL or an email address, none of which it had been given. The
/// scheduler is a shared component the site renders on every page, blog posts included, so the
/// destination is an in-page anchor and the reader is already on it.
///
/// <para>
/// Asserted on the rendered prompt, per this folder's standard: the article is a model output and
/// proves nothing about what the prompt asked for. All four consumers of the instruction are
/// covered, because the two Jeff reviewed (a Tool page and a blog post) are two of the four and a
/// closing that regresses on the other two is the same defect.
/// </para>
/// </summary>
public class ContentPromptBuilderClosingCtaTests
{
    private const string Anchor = "#consultationAppointment2xl";

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "ai implementation",
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: LlmProviderType.OpenAi,
        ConsultationAnchorHref: Anchor,
        ConsultationCtaLabel: "Schedule a Free Consultation");

    /// <summary>
    /// The whole rendered prompt, both messages. The closing instruction sits on the system message
    /// for a pillar section and on the user message for a blog body, and which of the two carries it
    /// is not what is being asserted here.
    /// </summary>
    private static string SystemPrompt(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    private static string PillarPrompt(ProjectGenerationContext context) =>
        SystemPrompt(new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            context,
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview", "Details"]),
            slots: [SectionSlot.Assigned("Details")],
            fullOutline: [SectionSlot.Assigned("Overview"), SectionSlot.Assigned("Details")],
            isRegeneration: false));

    /// <summary>
    /// A pillar batch that does NOT own the page's final section: the batch ends on "Overview" while the
    /// outline ends on "Details". Needed because every other helper here owns its closing, so nothing
    /// would catch an instruction that leaked into every batch of a batched page.
    /// </summary>
    private static string PillarBatchThatDoesNotCloseThePage(ProjectGenerationContext context) =>
        SystemPrompt(new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            context,
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview", "Details"]),
            slots: [SectionSlot.Assigned("Overview")],
            fullOutline: [SectionSlot.Assigned("Overview"), SectionSlot.Assigned("Details")],
            isRegeneration: false));

    private static string SingleSectionPrompt(ProjectGenerationContext context) =>
        SystemPrompt(new ContentPromptBuilder().BuildArticleSectionPrompt(
            context,
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview"]),
            sectionHeading: "Overview",
            sectionIndex: 0,
            totalSections: 1,
            fullOutline: ["Overview"],
            isRegeneration: false));

    private static string BlogPrompt(ProjectGenerationContext context) =>
        SystemPrompt(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            context, new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview", "Details"])));

    private static string ToolPrompt(ProjectGenerationContext context)
    {
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        return SystemPrompt(new ContentPromptBuilder().BuildToolBodyPrompt(
            context,
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            ToolPrompts.Outline(context, app.Name)));
    }

    public static TheoryData<string> AllFourBodyPrompts() => new()
    {
        "pillar", "section", "blog", "tool",
    };

    private static string Render(string which, ProjectGenerationContext context) => which switch
    {
        "pillar" => PillarPrompt(context),
        "section" => SingleSectionPrompt(context),
        "blog" => BlogPrompt(context),
        "tool" => ToolPrompt(context),
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown body prompt"),
    };

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void Every_body_prompt_closes_on_the_on_page_scheduler(string which)
    {
        var system = Render(which, Context());

        Assert.Contains("CLOSING:", system, StringComparison.Ordinal);
        Assert.Contains($"href \"{Anchor}\"", system, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void Every_body_prompt_tells_the_writer_a_link_sits_on_a_few_words(string which)
    {
        // The Stampli tool page of 2026-10-05 came back with whole paragraphs as links, and again on
        // 2026-10-09 after the instruction was sharpened. The writer now names a target id and the
        // anchor words and never an address; the limit is the guard's own number, so the writer is
        // never told one thing and refused for another.
        var system = Render(which, Context());

        Assert.Contains(ContentPromptBuilder.LinkTextInstruction, system, StringComparison.Ordinal);
        Assert.Contains(
            $"{GccDraftGuard.MaxLinkWords} words at most", ContentPromptBuilder.LinkTextInstruction, StringComparison.Ordinal);
        Assert.Contains("YOU NEVER WRITE ITS ADDRESS", system, StringComparison.Ordinal);
        Assert.Contains("Write no URL and no path anywhere", system, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void Every_body_prompt_states_the_us_dollar_rule(string which)
    {
        Assert.Contains(ContentPromptBuilder.CurrencyInstruction, Render(which, Context()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_part_that_paraphrases_the_partner_links_the_source_name_not_the_paragraph()
    {
        // "attribute it, with the page it comes from as that run's href" is the sentence that produced
        // the paragraph-long links: the paraphrase is one run, so the whole of it carried the URL.
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        var outline = ToolPrompts.Outline(Context(), app.Name);
        var later = SystemPrompt(new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            outline,
            batchIndex: 1));

        Assert.Contains("NO QUOTATION IN THIS PART", later, StringComparison.Ordinal);
        Assert.Contains("in a short run of its own", later, StringComparison.Ordinal);
        Assert.DoesNotContain("attribute it, with the page it comes from", later, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void Every_body_prompt_forbids_sending_the_reader_somewhere_else(string which)
    {
        var system = Render(which, Context());

        // The three the drafts actually did: a site, a contact page, an email address.
        Assert.Contains("visit our site", system, StringComparison.Ordinal);
        Assert.Contains("head over to our contact page", system, StringComparison.Ordinal);
        Assert.Contains("never an email address", system, StringComparison.Ordinal);
        Assert.Contains("blog posts included", system, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_scheduler_the_closing_names_no_destination_at_all()
    {
        // No anchor configured is "we do not know where this goes", and a URL the writer supplies
        // itself is a link to a page that does not exist. Silence beats invention.
        var system = BlogPrompt(Context() with
        {
            ConsultationAnchorHref = null,
            ConsultationCtaLabel = null,
        });

        Assert.Contains("CLOSING:", system, StringComparison.Ordinal);
        Assert.Contains("Name no destination", system, StringComparison.Ordinal);
        Assert.DoesNotContain(Anchor, system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_brief_still_owns_the_ask_and_the_anchor_still_owns_the_destination()
    {
        // ctaType/ctaLabel are the operator's words for what is being asked for. The anchor answers
        // a different question -- where the ask lands -- so naming one must not silence the other.
        var system = BlogPrompt(Context() with
        {
            CtaType = "book_appointment",
            CtaLabel = "Book your assessment",
        });

        Assert.Contains("asking for book_appointment", system, StringComparison.Ordinal);
        Assert.Contains("worded as \"Book your assessment\"", system, StringComparison.Ordinal);
        Assert.Contains($"href \"{Anchor}\"", system, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_brief_cta_the_scheduler_label_becomes_the_ask()
    {
        // "the one action this reader should take next" is not an ask, it is a placeholder for one.
        // Where the publisher has a scheduler, the ask it is named for is the ask.
        var system = BlogPrompt(Context());

        Assert.Contains("worded as \"Schedule a Free Consultation\"", system, StringComparison.Ordinal);
        Assert.DoesNotContain("the one action this reader should take next", system, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------------------
    // A page that builds its own closing (Content Creator, Jeff 2026-10-07). The questions the operator
    // asks a new client, and the booking line they follow, are added by GccClosing; the writer ends its
    // last section on its own material and is handed neither the CTA setting nor the questions. The
    // tests of the page's own closing are in GccClosingLineTests.
    // ----------------------------------------------------------------------------------------------

    private static ProjectGenerationContext PageBuildsItsClosing() =>
        Context() with { PageBuildsClosing = true, CtaType = "book_now", CtaLabel = "Book a consult" };

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void A_page_that_builds_its_closing_tells_the_writer_to_end_on_its_own_material(string which)
    {
        var system = Render(which, PageBuildsItsClosing());

        Assert.Contains("END OF THE PAGE: the page adds its own booking line after your last section", system, StringComparison.Ordinal);
        Assert.DoesNotContain("CLOSING:", system, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void A_page_that_builds_its_closing_never_shows_the_writer_the_cta_setting_or_the_scheduler_anchor(string which)
    {
        var system = Render(which, PageBuildsItsClosing());

        Assert.DoesNotContain("book_now", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Book a consult", system, StringComparison.Ordinal);
        Assert.DoesNotContain("CTA:", system, StringComparison.Ordinal);
        Assert.DoesNotContain(Anchor, system, StringComparison.Ordinal);
        Assert.DoesNotContain("weave naturally into closing", system, StringComparison.Ordinal);
    }

    [Fact]
    public void A_batch_that_does_not_end_the_page_is_told_the_page_adds_the_closing()
    {
        var doesNotClose = PillarBatchThatDoesNotCloseThePage(PageBuildsItsClosing());
        var closes = PillarPrompt(PageBuildsItsClosing());

        Assert.Contains("This call does not end the page", doesNotClose, StringComparison.Ordinal);
        Assert.Contains("The page adds its own closing after its final section.", doesNotClose, StringComparison.Ordinal);
        Assert.DoesNotContain("END OF THE PAGE", doesNotClose, StringComparison.Ordinal);
        Assert.Contains("END OF THE PAGE", closes, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_final_single_section_call_is_told_to_end_the_page()
    {
        var context = PageBuildsItsClosing();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["A", "B", "C"]);
        var builder = new ContentPromptBuilder();

        var middle = SystemPrompt(builder.BuildArticleSectionPrompt(
            context, metadata, sectionHeading: "B", sectionIndex: 1, totalSections: 3,
            fullOutline: ["A", "B", "C"], isRegeneration: false));
        var last = SystemPrompt(builder.BuildArticleSectionPrompt(
            context, metadata, sectionHeading: "C", sectionIndex: 2, totalSections: 3,
            fullOutline: ["A", "B", "C"], isRegeneration: false));

        Assert.DoesNotContain("END OF THE PAGE", middle, StringComparison.Ordinal);
        Assert.Contains("END OF THE PAGE", last, StringComparison.Ordinal);
    }

    [Fact]
    public void The_workflow_products_writer_is_still_asked_for_its_closing()
    {
        // PageBuildsClosing is false for the Workflow product, whose pages have no GccClosing: its
        // behaviour and its asks are the ones the tests above pin.
        var system = BlogPrompt(Context());

        Assert.Contains("CLOSING:", system, StringComparison.Ordinal);
        Assert.DoesNotContain("END OF THE PAGE", system, StringComparison.Ordinal);
    }
}
