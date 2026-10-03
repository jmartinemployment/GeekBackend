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
    // The practical client diagnosis -- Jeff, 2026-10-03: "A useful discovery question set as the CTA."
    //
    // The instruction above had always demanded one plain ask and banned the reflection endings every
    // draft produced, while supplying nothing for the ask to be ABOUT. These are the questions the
    // operator actually asks a prospect, so the closing can be a diagnostic the reader runs.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Three of Jeff's eight for AP approval workflows, verbatim.</summary>
    private static readonly string[] Diagnosis =
    [
        "How many invoices per month require someone's approval?",
        "Who approves spending, and what happens when they are unavailable?",
        "Is there an audit trail sufficient to answer \"who approved this payment and why?\"",
    ];

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void Every_body_prompt_hands_the_reader_the_operators_diagnosis(string which)
    {
        var system = Render(which, Context() with { DiagnosisQuestions = Diagnosis });

        // Every question, verbatim. A reader that drops one is indistinguishable from an operator who
        // never typed it.
        foreach (var question in Diagnosis)
        {
            Assert.Contains(question, system, StringComparison.Ordinal);
        }

        Assert.Contains("practical diagnosis", system, StringComparison.Ordinal);
        // The questions are material for the ask, not a replacement for it.
        Assert.Contains("CLOSING:", system, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllFourBodyPrompts))]
    public void An_empty_diagnosis_leaves_the_closing_byte_identical(string which)
    {
        // This is additive on every path. A create that never touches the field must build the prompt it
        // built yesterday -- and an empty list must behave as absence, since that is what the frontend
        // sends for a textarea the operator opened and left alone.
        var absent = Render(which, Context());
        var empty = Render(which, Context() with { DiagnosisQuestions = [] });

        Assert.Equal(absent, empty);
        Assert.DoesNotContain("practical diagnosis", absent, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_batch_that_writes_the_last_section_gets_the_diagnosis()
    {
        // The questions inherit OwnsTheClosing rather than carrying their own gate. Without that, a
        // batched pillar asks the reader the same eight questions in every batch -- the three-sign-offs
        // failure the gate was built for, with eight questions attached to each one.
        var context = Context() with { DiagnosisQuestions = Diagnosis };

        var closes = PillarPrompt(context);
        var doesNotClose = PillarBatchThatDoesNotCloseThePage(context);

        Assert.Contains(Diagnosis[0], closes, StringComparison.Ordinal);
        Assert.DoesNotContain(Diagnosis[0], doesNotClose, StringComparison.Ordinal);
        Assert.Contains("This call does not end the page", doesNotClose, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_final_single_section_call_gets_the_closing()
    {
        // BuildArticleSectionPrompt appended the closing unconditionally while the batch builder gated
        // it behind OwnsTheClosing. That was survivable while the closing was one ask; once the
        // diagnosis joined it, six of a pillar's seven sections were handed the operator's discovery
        // questions and told to pose them. Code review, 2026-10-03.
        var context = Context() with { DiagnosisQuestions = Diagnosis };
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["A", "B", "C"]);
        var builder = new ContentPromptBuilder();

        var middle = SystemPrompt(builder.BuildArticleSectionPrompt(
            context, metadata, sectionHeading: "B", sectionIndex: 1, totalSections: 3,
            fullOutline: ["A", "B", "C"], isRegeneration: false));
        var last = SystemPrompt(builder.BuildArticleSectionPrompt(
            context, metadata, sectionHeading: "C", sectionIndex: 2, totalSections: 3,
            fullOutline: ["A", "B", "C"], isRegeneration: false));

        Assert.DoesNotContain("CLOSING:", middle, StringComparison.Ordinal);
        Assert.DoesNotContain(Diagnosis[0], middle, StringComparison.Ordinal);
        Assert.Contains("This call does not end the page", middle, StringComparison.Ordinal);

        Assert.Contains("CLOSING:", last, StringComparison.Ordinal);
        Assert.Contains(Diagnosis[0], last, StringComparison.Ordinal);
    }

    [Fact]
    public void The_supplied_set_is_the_whole_set_the_writer_may_use()
    {
        // Selecting among them is allowed -- a 450-word closing cannot carry eight questions and an ask.
        // Composing a ninth is not: on the page it is indistinguishable from the eight that were
        // researched, which is the same reason framing is labelled "argue from it, never cite it".
        var system = BlogPrompt(Context() with { DiagnosisQuestions = Diagnosis });

        Assert.Contains("or a subset of them", system, StringComparison.Ordinal);
        Assert.Contains("Do NOT invent a question that is not in that list", system, StringComparison.Ordinal);
        Assert.Contains("form the page administers", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_diagnosis_does_not_replace_the_ask_or_its_destination()
    {
        // "as the CTA" means the questions are what the ask is about, not that they are the ask. A
        // closing that ends on a question has asked for nothing.
        var system = BlogPrompt(Context() with
        {
            CtaType = "book_appointment",
            CtaLabel = "Book your assessment",
            DiagnosisQuestions = Diagnosis,
        });

        Assert.Contains("asking for book_appointment", system, StringComparison.Ordinal);
        Assert.Contains("worded as \"Book your assessment\"", system, StringComparison.Ordinal);
        Assert.Contains($"href \"{Anchor}\"", system, StringComparison.Ordinal);
        Assert.Contains("The ask still closes the section after them", system, StringComparison.Ordinal);
    }
}
