using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// FAQ fields for the blog and the tool pages (Jeff, 2026-10-08: "Add Blog, Tool FAQ's"). The pillar
/// answered the brief's People Also Ask list; the blog had no FAQ, and the tool page's FAQ came only
/// from the vendor's extracted bank. Now the brief carries <c>blogFaqQuestions</c>, answered at the end
/// of the blog the way the pillar's are, and each tool's entry carries <c>faqQuestions</c>, answered on
/// that page from the partner's retrieved pages alone.
/// </summary>
public class GccFaqFromOperatorQuestionsTests
{
    private static readonly ContentPromptBuilder Builder = new();

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "automated payment execution",
        Department: "accounting",
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
        Provider: LlmProviderType.OpenAi);

    [Fact]
    public void The_blogs_faq_questions_are_read_from_the_brief_as_lines_or_an_array()
    {
        var lines = GccGenerateService.ExtractBriefFields(
            """{"blogFaqQuestions":"How long does a rollout take?\nWhat does it cost to run?"}""");
        var array = GccGenerateService.ExtractBriefFields(
            """{"blogFaqQuestions":["How long does a rollout take?"," What does it cost to run? "]}""");
        var none = GccGenerateService.ExtractBriefFields("""{"paaQuestions":["A pillar question?"]}""");

        Assert.Equal(["How long does a rollout take?", "What does it cost to run?"], lines.BlogFaqQuestions);
        Assert.Equal(["How long does a rollout take?", "What does it cost to run?"], array.BlogFaqQuestions);
        Assert.Null(none.BlogFaqQuestions);
        Assert.Equal(["A pillar question?"], none.PaaQuestions);
    }

    [Fact]
    public void The_blog_faq_prompt_is_the_pillars_shape_on_a_blog_under_its_own_heading()
    {
        var request = Builder.BuildBlogFaqSectionPrompt(
            Context(),
            new BlogMetadataDraft("A Blog Title", "Meta", ["ai"], ["A"]),
            ["How long does a rollout take?", "What does it cost to run?"]);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Write ONLY the \"Frequently Asked Questions\" FAQ section of a BlogPosting blog.", system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"Frequently Asked Questions\"", system, StringComparison.Ordinal);
        Assert.DoesNotContain("People Also Ask", system, StringComparison.Ordinal);
        Assert.Contains("Article title: A Blog Title", user, StringComparison.Ordinal);
        Assert.Contains("- Q1: How long does a rollout take?", user, StringComparison.Ordinal);
        Assert.Contains("- Q2: What does it cost to run?", user, StringComparison.Ordinal);
        Assert.Equal(3072, request.MaxOutputTokens);
    }

    [Fact]
    public void The_pillars_faq_prompt_is_unchanged_by_the_shared_builder()
    {
        var request = Builder.BuildArticleFaqSectionPrompt(
            Context(), new ArticleMetadataDraft("A Pillar", "Meta", ["ai"], ["A"]), ["Q?"], isRegeneration: false);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Write ONLY the \"People Also Ask\" FAQ section of a TechnicalArticle pillar.", system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"People Also Ask\"", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_faq_prompt_answers_only_from_the_partners_evidence_and_leaves_the_rest_out()
    {
        var request = Builder.BuildToolFaqFromQuestionsPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", null, "https://partner.test/widget"),
            ["Does it sync with QuickBooks Online?", "Does it fly?"],
            "[Partner Widget] (https://partner.test/widget)\n  - Partner Widget syncs every payment to QuickBooks Online.");

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Answer ONLY from the PARTNER EVIDENCE", system, StringComparison.Ordinal);
        Assert.Contains("LEAVE THAT QUESTION OUT", system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"Frequently Asked Questions\"", system, StringComparison.Ordinal);
        Assert.Contains("=== PARTNER EVIDENCE", user, StringComparison.Ordinal);
        Assert.Contains("syncs every payment to QuickBooks Online", user, StringComparison.Ordinal);
        Assert.Contains("- Q2: Does it fly?", user, StringComparison.Ordinal);
        Assert.Contains("Page topic: Partner Widget", user, StringComparison.Ordinal);
        Assert.DoesNotContain("Pillar topic", user, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_faq_prompt_with_no_evidence_says_so_instead_of_inviting_an_answer()
    {
        var request = Builder.BuildToolFaqFromQuestionsPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", null, "https://partner.test/widget"),
            ["Does it fly?"],
            string.Empty);

        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("(nothing was retrieved -- answer no question)", user, StringComparison.Ordinal);
    }
}
