using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The opening is written against the retrieved evidence, not from brief text alone.
///
/// <para>
/// It was not until 2026-09-29. <c>GccGenerateService</c> built the evidence block and handed it
/// only to the body, so the lede and introduction -- the most-read paragraphs on the page, and the
/// ones that set every factual claim after them -- were written with the evidence sitting in a
/// local variable three lines above the call. The pillar lede prompt has always carried "a number
/// may appear only if it is in the supplied evidence or published by this publisher", and no
/// evidence was ever supplied to it: the one rule bounding its figures could neither be met nor
/// broken.
/// </para>
///
/// <para>
/// Asserted on the rendered prompt, per this folder's standard. What the model returns afterwards
/// is model output and proves nothing about what it was asked for.
/// </para>
/// </summary>
public class LedeEvidenceTests
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
        ConsultationCtaLabel: "Schedule a Free Consultation");

    private static string Rendered(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    /// <summary>A stand-in for what BuildResearchBlock renders, matched on its own marker line.</summary>
    private const string Evidence =
        "=== QUOTEABLE RESEARCH (partner/tool evidence) ===\n"
        + "[Zone & Co pricing] (https://zoneandco.test/pricing)\n"
        + "  Specific detail: Billing runs close in under four hours.";

    private static string PillarLede(string? evidence) =>
        Rendered(new ContentPromptBuilder().BuildPillarLedePrompt(
            Context(),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Opening", "What breaks"]),
            ledeHeading: "Opening",
            ledeIndex: 0,
            totalSections: 2,
            fullOutline: [SectionSlot.Assigned("Opening"), SectionSlot.Assigned("What breaks")],
            isRegeneration: false,
            evidenceBlock: evidence));

    private static string BlogLede(string? evidence) =>
        Rendered(new ContentPromptBuilder().BuildStandaloneBlogLedePrompt(
            Context(),
            new BlogMetadataDraft("Title", "Meta", ["ai"], ["Where the hours go"]),
            evidence));

    [Fact]
    public void The_pillar_opening_is_shown_the_retrieved_evidence()
    {
        var prompt = PillarLede(Evidence);

        Assert.Contains("QUOTEABLE RESEARCH", prompt, StringComparison.Ordinal);
        Assert.Contains("https://zoneandco.test/pricing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_blog_opening_is_shown_the_retrieved_evidence()
    {
        var prompt = BlogLede(Evidence);

        Assert.Contains("QUOTEABLE RESEARCH", prompt, StringComparison.Ordinal);
        Assert.Contains("https://zoneandco.test/pricing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_evidence_is_framed_for_an_opening_before_it_is_shown()
    {
        // The research block is written for the body: its rule 5 says a tool with a Target Entity
        // Match line "is named in the piece and its claims are cited from those passages". True of
        // a whole article, wrong in three paragraphs -- unframed, the opening would answer it by
        // listing every partner. So the framing must precede the block, not trail it.
        var prompt = PillarLede(Evidence);

        var framing = prompt.IndexOf("HOW TO USE THE EVIDENCE BELOW IN THE OPENING", StringComparison.Ordinal);
        var block = prompt.IndexOf("=== QUOTEABLE RESEARCH", StringComparison.Ordinal);

        Assert.True(framing >= 0, "The opening was handed the evidence with no instruction on how to read it.");
        Assert.True(block > framing, "The framing must come before the evidence it frames.");
    }

    [Fact]
    public void The_opening_is_told_not_to_name_partners_and_not_to_invent_a_figure()
    {
        var prompt = PillarLede(Evidence);

        Assert.Contains("names no partner or tool unless", prompt, StringComparison.Ordinal);
        Assert.Contains("must appear in a passage below", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_create_with_no_research_asks_for_the_opening_exactly_as_before()
    {
        // The channel licenses, it does not require -- the same property the retrieved-evidence
        // provenance channel was built with. A create with no research must not acquire a new
        // instruction about evidence it does not have.
        var withNothing = PillarLede(null);
        var withBlank = PillarLede("   ");

        Assert.DoesNotContain("HOW TO USE THE EVIDENCE BELOW", withNothing, StringComparison.Ordinal);
        Assert.Equal(withNothing, withBlank);
    }

    private static string ToolLede(string? evidence) =>
        Rendered(new ContentPromptBuilder().BuildArticleLedePrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            evidenceBlock: evidence));

    [Fact]
    public void The_tool_opening_is_shown_the_retrieved_evidence()
    {
        // Tool reaches this builder through ToolPrompts.Lede. It used to call it directly from
        // GccGenerateService, which both left ToolPrompts.Lede with no callers and gave the opening
        // nothing to be true about.
        var prompt = ToolLede(Evidence);

        Assert.Contains("HOW TO USE THE EVIDENCE BELOW IN THE OPENING", prompt, StringComparison.Ordinal);
        Assert.Contains("QUOTEABLE RESEARCH", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_page_with_no_research_asks_for_the_opening_exactly_as_before()
    {
        Assert.Equal(ToolLede(null), ToolLede("  "));
        Assert.DoesNotContain("HOW TO USE THE EVIDENCE BELOW", ToolLede(null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_body_is_shown_the_retrieved_evidence()
    {
        // BuildToolBodyPrompt has always appended this block; nothing ever passed one. Tool is the
        // only type where a citeable blockquote is required, and the passages it would cite from
        // were the ones not arriving.
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        var outline = ToolPrompts.Outline(Context(), app.Name);
        var prompt = Rendered(new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            outline: outline,
            fullOutline: outline,
            evidenceBlock: Evidence));

        Assert.Contains("QUOTEABLE RESEARCH", prompt, StringComparison.Ordinal);
        Assert.Contains("https://zoneandco.test/pricing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_opening_is_not_shown_competitor_heading_structure()
    {
        // BuildCompetitorHeadingBlock tells the model to tag a heading "competitor:<exact heading
        // text>" and refers it to the provenance rules. The lede contract has no provenance field
        // and this prompt states no provenance rules, so that block would cite instructions the
        // model was never given. Only the research half is passed in.
        var prompt = PillarLede(Evidence);

        Assert.DoesNotContain("COMPETITOR HEADING STRUCTURE", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("competitor:<exact heading text>", prompt, StringComparison.Ordinal);
    }
}
