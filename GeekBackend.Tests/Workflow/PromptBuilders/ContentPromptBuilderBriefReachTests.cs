using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The brief's control fields reach every long-form body prompt.
///
/// <para>
/// They did not reach the tool body. <c>BuildBriefBodyGuidance</c> is the one rendering of them —
/// primary intent, buying stage, tone of voice, E-E-A-T signals, CTA, length band, writing notes —
/// and eight prompts called it. <c>BuildToolBodyPrompt</c> was not one of them, so all seven were
/// collected by the UI, stored, and then simply not shown to the writer of the type Jeff calls the
/// most important, while pillar and blog got every one.
/// </para>
///
/// <para>
/// The gap read as covered because the tool prompt has an audience block of its own naming the
/// segment and the call to action. Neither is among the seven — the segment is not in that block at
/// all — so what looked like the brief arriving was a different, smaller set of fields.
/// </para>
/// </summary>
public class ContentPromptBuilderBriefReachTests
{
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
        ConsultationAnchorHref: "#consultationAppointment2xl",
        ConsultationCtaLabel: "Schedule a Free Consultation")
    {
        AudienceSegment = "SMB finance leads",
        AudienceNotes = "time-poor, budget-conscious",
        ContentAngle = "problem_solution",
        PrimaryIntent = "commercial_investigation",
        SecondaryIntent = "informational",
        BuyingStage = "consideration",
        ToneOfVoice = "consultant_professional",
        EeatSignals = ["experience", "expertise"],
        LengthBand = "long",
        WritingNotes = "Avoid jargon in the first two paragraphs.",
    };

    private static string Rendered(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    private static string PillarBody() =>
        Rendered(new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            Context(),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["One", "Two"]),
            slots: [SectionSlot.Assigned("One")],
            fullOutline: [SectionSlot.Assigned("One"), SectionSlot.Assigned("Two")],
            isRegeneration: false));

    private static string BlogBody() =>
        Rendered(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(), new BlogMetadataDraft("Title", "Meta", ["ai"], ["One", "Two"])));

    private static string ToolBody()
    {
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        return Rendered(new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            ToolPrompts.Outline(Context(), app.Name)));
    }

    public static TheoryData<string> EveryLongFormBody() => new() { "pillar", "blog", "tool" };

    private static string Render(string type) => type switch
    {
        "pillar" => PillarBody(),
        "blog" => BlogBody(),
        "tool" => ToolBody(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown body prompt"),
    };

    [Theory]
    [MemberData(nameof(EveryLongFormBody))]
    public void Every_long_form_body_is_shown_the_briefs_control_fields(string type)
    {
        var prompt = Render(type);

        Assert.Contains("=== BRIEF CONTROLS (honor in body) ===", prompt, StringComparison.Ordinal);
        Assert.Contains("Primary intent: commercial_investigation", prompt, StringComparison.Ordinal);
        Assert.Contains("Buying stage: consideration", prompt, StringComparison.Ordinal);
        Assert.Contains("Tone of voice: consultant_professional", prompt, StringComparison.Ordinal);
        Assert.Contains("E-E-A-T signals to demonstrate: experience, expertise", prompt, StringComparison.Ordinal);
        Assert.Contains("Length band: long", prompt, StringComparison.Ordinal);
        Assert.Contains("Writing notes: Avoid jargon in the first two paragraphs.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_body_still_names_who_the_page_is_for()
    {
        // The audience segment is not one of the control fields, so adding the shared block does
        // not cover it. It reaches the tool body through the page's own audience block and nowhere
        // else — dropping that line as a duplicate would have removed the only copy.
        var prompt = ToolBody();

        Assert.Contains("WHO THIS IS FOR: SMB finance leads", prompt, StringComparison.Ordinal);
        Assert.Contains("time-poor, budget-conscious", prompt, StringComparison.Ordinal);
    }
}
