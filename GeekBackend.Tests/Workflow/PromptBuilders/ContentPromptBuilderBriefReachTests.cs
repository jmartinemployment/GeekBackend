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
/// the audience, primary intent, buying stage, tone of voice, E-E-A-T signals and length band —
/// and eight prompts called it. <c>BuildToolBodyPrompt</c> was not one of them, so all of them were
/// collected by the UI, stored, and then simply not shown to the writer of the type Jeff calls the
/// most important, while pillar and blog got every one.
/// </para>
///
/// <para>
/// The gap read as covered because the tool prompt had an audience block of its own naming the
/// segment and asking for a closing. Neither was that rendering, so a reader checking those two
/// found them.
/// </para>
///
/// <para>
/// The audience segment went the other way: it reached the lede prompts and the tool page's own
/// block, and no other body — so a pillar and a blog were written to a reader the operator had
/// named and the writer had never been told about. It belongs to the same rendering as the rest of
/// the brief, which is where it is now; Tool's copy is gone rather than kept beside it.
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
        AudienceDetails = ["budget-conscious", "time-poor"],
        AudienceNotes = "weighing a first automation purchase",
        ContentAngle = "problem_solution",
        PrimaryIntent = "commercial_investigation",
        SecondaryIntent = "informational",
        BuyingStage = "consideration",
        ToneOfVoice = "consultant_professional",
        EeatSignals = ["experience", "expertise"],
        LengthBand = "long",
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
        Assert.Contains("WHO THIS IS FOR: SMB finance leads", prompt, StringComparison.Ordinal);
        Assert.Contains("notes: weighing a first automation purchase", prompt, StringComparison.Ordinal);
        Assert.Contains("Primary intent: commercial_investigation", prompt, StringComparison.Ordinal);
        Assert.Contains("Buying stage: consideration", prompt, StringComparison.Ordinal);
        Assert.Contains("Tone of voice: consultant_professional", prompt, StringComparison.Ordinal);
        Assert.Contains("E-E-A-T signals to demonstrate: experience, expertise", prompt, StringComparison.Ordinal);
        Assert.Contains("Length band: long", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryLongFormBody))]
    public void Every_long_form_body_is_shown_the_audiences_details(string type)
    {
        // The details list is the half the tool page's own copy dropped, which is the argument for
        // one rendering rather than a per-type line that covers most of a field.
        Assert.Contains("details: budget-conscious, time-poor", Render(type), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_body_names_who_the_page_is_for_exactly_once()
    {
        // Its own copy is gone now that the shared rendering carries it. Two copies of one fact in
        // one prompt is how the two drift, and the copy here was already the poorer of them.
        var prompt = ToolBody();

        Assert.Equal(1, prompt.Split("WHO THIS IS FOR:").Length - 1);
        Assert.Equal(1, prompt.Split("SMB finance leads").Length - 1);
    }
}
