using GeekAPI.Services.ContentCreator.ContentTypes;
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
}
