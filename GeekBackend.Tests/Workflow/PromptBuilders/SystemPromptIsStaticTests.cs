using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The system message is the same text on every call that shares a contract: not a word of it depends on
/// the project, the page, the batch, the evidence or a revision. That is what keeps the rules out from
/// between the data they govern, and what lets a provider's prompt cache read the whole prefix from the
/// second call on. The run's own data is in the user message.
/// </summary>
public class SystemPromptIsStaticTests
{
    private static ProjectGenerationContext Context(string keyword, string publisher, string positioning) => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: keyword,
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [$"{publisher} framework heading"],
        CrawledParagraphs: [$"{publisher} says its method has four phases."],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: publisher,
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: positioning,
        Provider: LlmProviderType.OpenAi)
    {
        PrimaryIntent = "commercial_investigation",
        ContentAngle = "problem_solution",
        AudienceNotes = $"Buyers of {keyword}.",
    };

    private static readonly ProjectGenerationContext One =
        Context("accounts payable automation", "Geek", "an AI implementation partner");

    private static readonly ProjectGenerationContext Another =
        Context("expense approval routing", "Zephyr", "a workflow consultancy");

    private static string System(ChatCompletionRequest r) => r.Messages.Single(m => m.Role == ChatRole.System).Content;

    private static string User(ChatCompletionRequest r) => r.Messages.Single(m => m.Role == ChatRole.User).Content;

    private static readonly ArticleMetadataDraft Article = new("A Title", "A meta", ["ai"], ["a"]);
    private static readonly BlogMetadataDraft Blog = new("A Blog Title", "A meta", ["ai"], ["a"]);
    private static readonly SoftwareApplicationDescriptor App = new("Partner Widget", "A widget.");

    private static readonly Section Opening = new(
        "h2", "The invoice that sat in a drawer", [new TextParagraph([new Run("It was still there on Friday.")])], null, []);

    private static ChatCompletionRequest PillarBatch(
        ProjectGenerationContext c, SectionSlot slot, int batch, string? evidence, string? notes, Section? lede) =>
        new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            c, Article, [slot], [SectionSlot.Cover("the opening"), slot],
            isRegeneration: notes is not null, revisionNotes: notes, requireHeadingProvenance: true,
            evidenceBlock: evidence, lede: lede, batchIndex: batch);

    private static ChatCompletionRequest BlogBody(
        ProjectGenerationContext c, SectionSlot slot, int batch, string? evidence, string? notes, Section? lede) =>
        new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            c, Blog, revisionNotes: notes, requireHeadingProvenance: true, evidenceBlock: evidence, lede: lede,
            sectionBatch: [slot], batchIndex: batch, fullOutline: [SectionSlot.Cover("the opening"), slot]);

    private static ChatCompletionRequest ToolBody(
        ProjectGenerationContext c, SectionSlot slot, int batch, string? evidence, string? notes, Section? lede) =>
        new ContentPromptBuilder().BuildToolBodyPrompt(
            c, Article, App, "partner-widget", [slot], revisionNotes: notes,
            extractedToolResearchJson: """{"caseStudies":[]}""", lede: lede,
            fullOutline: [SectionSlot.Cover("the opening"), slot], batchIndex: batch, evidenceBlock: evidence);

    public static TheoryData<string> Bodies => new() { "pillar", "blog", "tool" };

    private static ChatCompletionRequest Body(
        string type, ProjectGenerationContext c, string slotText, int batch, string? evidence, string? notes, Section? lede)
    {
        var slot = SectionSlot.Cover(slotText);
        return type switch
        {
            "pillar" => PillarBatch(c, slot, batch, evidence, notes, lede),
            "blog" => BlogBody(c, slot, batch, evidence, notes, lede),
            _ => ToolBody(c, slot, batch, evidence, notes, lede),
        };
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void A_body_calls_system_message_is_the_same_for_two_batches_two_projects_and_a_revision(string type)
    {
        var first = Body(type, One, "what the delay costs", 0, null, null, null);
        var laterBatch = Body(type, One, "how the approach works", 1, "EVIDENCE: a competitor says X.", null, Opening);
        var otherProject = Body(type, Another, "what the delay costs", 0, "EVIDENCE: other.", null, null);
        var revision = Body(type, One, "what the delay costs", 0, null, "Make section one more concrete.", Opening);

        Assert.Equal(System(first), System(laterBatch));
        Assert.Equal(System(first), System(otherProject));
        Assert.Equal(System(first), System(revision));
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void None_of_a_runs_own_data_is_in_the_system_message(string type)
    {
        var request = Body(type, One, "what the delay costs", 0, "EVIDENCE: a competitor says X.", "Make it concrete.", Opening);
        var system = System(request);

        foreach (var runData in new[]
                 {
                     "accounts payable automation", "Geek", "an AI implementation partner", "A Title", "A Blog Title",
                     "Partner Widget", "EVIDENCE: a competitor says X.", "Make it concrete.", "what the delay costs",
                     "BRIEF CONTROLS", "THE OPENING THIS PAGE ALREADY HAS", "framework heading", "CLOSING:",
                     "WHAT THIS PAGE IS SCORED ON", "NO TOOLS SECTION", "TOOLS ARE THE SOLUTION",
                 })
        {
            Assert.DoesNotContain(runData, system, StringComparison.Ordinal);
        }

        // And it is in the user message, where it belongs.
        var user = User(request);
        Assert.Contains("accounts payable automation", user, StringComparison.Ordinal);
        Assert.Contains("EVIDENCE: a competitor says X.", user, StringComparison.Ordinal);
        Assert.Contains("Make it concrete.", user, StringComparison.Ordinal);
        Assert.Contains("THE OPENING THIS PAGE ALREADY HAS", user, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void The_system_message_holds_the_rules_that_never_change(string type)
    {
        var system = System(Body(type, One, "what the delay costs", 0, null, null, null));

        Assert.Contains("a senior consultant who knows this work", system, StringComparison.Ordinal);
        Assert.Contains("Ban filler", system, StringComparison.Ordinal);
        Assert.Contains("delve", system, StringComparison.Ordinal);
        Assert.Contains("MONEY IS IN US DOLLARS ONLY", system, StringComparison.Ordinal);
        // No rule about links: the reply has no field for one (Jeff, 2026-10-10), so there is nothing to say.
        Assert.DoesNotContain("A LINK IS A RUN", system, StringComparison.Ordinal);
        Assert.DoesNotContain("\"link\"", system, StringComparison.Ordinal);
        Assert.Contains("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
        Assert.Contains("GROUNDING:", system, StringComparison.Ordinal);
        Assert.Contains("CONTENT ONLY:", system, StringComparison.Ordinal);
        Assert.Contains("\"sections\"", system, StringComparison.Ordinal);
        // One voice. The second identity is gone.
        Assert.DoesNotContain("over coffee", system, StringComparison.Ordinal);
        Assert.DoesNotContain("content marketer", system, StringComparison.Ordinal);
    }

    [Fact]
    public void Provenance_is_in_the_contract_for_pillar_and_blog_and_not_for_tool()
    {
        var slot = SectionSlot.Cover("what the delay costs");

        Assert.Contains("\"provenance\"", System(PillarBatch(One, slot, 0, null, null, null)), StringComparison.Ordinal);
        Assert.Contains("\"provenance\"", System(BlogBody(One, slot, 0, null, null, null)), StringComparison.Ordinal);
        Assert.DoesNotContain("\"provenance\"", System(ToolBody(One, slot, 0, null, null, null)), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void The_user_message_ends_by_pointing_back_at_the_contract_not_on_the_evidence(string type)
    {
        var user = User(Body(type, One, "what the delay costs", 0, "EVIDENCE: a long vendor passage.", null, null)).TrimEnd();

        Assert.EndsWith(
            "Answer in the JSON the output contract in the system message describes. You write each heading yourself.",
            user,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_lede_calls_share_one_system_message_per_contract_whatever_the_page()
    {
        var builder = new ContentPromptBuilder();
        var other = Article with { Title = "Another" };

        var pillarOne = builder.BuildPillarLedePrompt(One, Article, "the opening", 0, 6, [SectionSlot.Cover("the opening")], false);
        var pillarTwo = builder.BuildPillarLedePrompt(Another, other, "the start", 0, 6, [SectionSlot.Cover("the start")], true, "Tighter.");
        var blogOne = builder.BuildStandaloneBlogLedePrompt(One, Blog, "EVIDENCE");
        var blogTwo = builder.BuildStandaloneBlogLedePrompt(Another, Blog);
        var toolOne = builder.BuildArticleLedePrompt(One, Article, evidenceBlock: "EVIDENCE");
        var toolTwo = builder.BuildArticleLedePrompt(Another, other);

        Assert.Equal(System(pillarOne), System(pillarTwo));
        Assert.Equal(System(blogOne), System(blogTwo));
        Assert.Equal(System(blogOne), System(toolOne));
        Assert.Equal(System(toolOne), System(toolTwo));
        Assert.DoesNotContain("Geek", System(pillarOne), StringComparison.Ordinal);
        Assert.Contains("\"introduction\"", System(pillarOne), StringComparison.Ordinal);
        Assert.Contains("\"ledeType\"", System(blogOne), StringComparison.Ordinal);
    }
}
