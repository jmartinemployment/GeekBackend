using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The point-of-law rule (<see cref="ContentPromptBuilder.PointOfLawInstruction"/>) is printed by the
/// pillar's, the blog's and the tool page's body prompts when the request carries an evidence block,
/// and by none of them when it does not.
/// </summary>
/// <remarks>
/// The gate is the evidence block because the rule is about the evidence: a point of law is quoted from
/// a passage in it, and a request with no passages has nothing to quote. The Workflow product's
/// requests to these same three builders carry no evidence block and have no step that would resolve
/// such a quotation (<c>GccQuotations</c> runs on the Content Creator's pages alone), so they must not
/// be told to write one.
/// </remarks>
public class PointOfLawInstructionTests
{
    private const string Evidence = "=== QUOTEABLE RESEARCH ===\nA passage.";

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

    private static readonly List<string> Outline = ["Where the hours go", "What changes first", "What to do next"];

    private static string Pillar(string? evidenceBlock) =>
        Rendered(new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            Context(),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], Outline),
            slots: [SectionSlot.Assigned(Outline[0])],
            fullOutline: [.. Outline.Select(SectionSlot.Assigned)],
            isRegeneration: false,
            evidenceBlock: evidenceBlock));

    private static string Blog(string? evidenceBlock) =>
        Rendered(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(),
            new BlogMetadataDraft("Title", "Meta", ["ai"], Outline),
            evidenceBlock: evidenceBlock,
            sectionBatch: [SectionSlot.Cover(Outline[0])],
            fullOutline: [.. Outline.Select(c => SectionSlot.Cover(c))]));

    private static string Tool(string? evidenceBlock, int batchIndex)
    {
        var context = Context();
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        var full = ToolPrompts.Outline(context, app.Name);
        return Rendered(new ContentPromptBuilder().BuildToolBodyPrompt(
            context,
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            [.. full.Take(2)],
            fullOutline: full,
            batchIndex: batchIndex,
            evidenceBlock: evidenceBlock));
    }

    [Fact]
    public void With_an_evidence_block_every_body_prompt_is_told_how_a_point_of_law_is_stated()
    {
        foreach (var prompt in new[] { Pillar(Evidence), Blog(Evidence), Tool(Evidence, 0), Tool(Evidence, 1) })
        {
            Assert.Contains(ContentPromptBuilder.PointOfLawInstruction, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Without_an_evidence_block_no_body_prompt_is_told_so_the_Workflow_products_writers_are_not()
    {
        foreach (var prompt in new[] { Pillar(null), Blog(null), Tool(null, 0), Tool(null, 1) })
        {
            Assert.DoesNotContain("A POINT OF LAW IS QUOTED FROM A PASSAGE", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_rule_names_the_consequence_the_writer_will_see_and_no_refusal()
    {
        Assert.Contains("a quotation that matches no passage is deleted from the page", ContentPromptBuilder.PointOfLawInstruction, StringComparison.Ordinal);
        Assert.Contains("say in that same sentence that the reader's accountant or employment attorney makes the call", ContentPromptBuilder.PointOfLawInstruction, StringComparison.Ordinal);
        Assert.Contains("never as a note, a disclaimer or a label", ContentPromptBuilder.PointOfLawInstruction, StringComparison.Ordinal);
        Assert.DoesNotContain("refus", ContentPromptBuilder.PointOfLawInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("—", ContentPromptBuilder.PointOfLawInstruction, StringComparison.Ordinal);
    }
}
