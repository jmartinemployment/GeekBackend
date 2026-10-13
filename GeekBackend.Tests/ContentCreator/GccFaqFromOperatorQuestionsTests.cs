using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// FAQ fields for the blog and the tool pages (Jeff, 2026-10-08: "Add Blog, Tool FAQ's"). The pillar
/// answered the brief's People Also Ask list; the blog had no FAQ, and the tool page's FAQ came only
/// from the vendor's extracted bank. The brief carries <c>blogFaqQuestions</c>, written at the end of
/// the blog the way the pillar's are, and each tool's entry carries <c>faqQuestions</c>, written on
/// that page.
/// <para>
/// 2026-10-13: every one of them is a question with the answer the operator entered for it, and the
/// request hands the writer both. These hold what the three requests say.
/// </para>
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

    private static readonly GccFaqEntry[] TwoAnswered =
    [
        new("How long does a rollout take?", "Six weeks for a team of ten."),
        new("What does it cost to run?", "Nothing beyond the licence."),
    ];

    [Fact]
    public void The_blogs_faq_is_read_from_the_brief_as_rows_of_question_and_answer_or_as_questions_alone()
    {
        var rows = GccGenerateService.ExtractBriefFields(
            """{"blogFaqQuestions":[{"question":" How long does a rollout take? ","answer":" Six weeks for a team of ten. "},{"question":"What does it cost to run?","answer":""}]}""");
        var lines = GccGenerateService.ExtractBriefFields(
            """{"blogFaqQuestions":"How long does a rollout take?\nWhat does it cost to run?"}""");
        var array = GccGenerateService.ExtractBriefFields(
            """{"blogFaqQuestions":["How long does a rollout take?"," What does it cost to run? "]}""");
        var none = GccGenerateService.ExtractBriefFields("""{"paaQuestions":["A pillar question?"]}""");

        Assert.Equal(
            [new GccFaqEntry("How long does a rollout take?", "Six weeks for a team of ten."), new GccFaqEntry("What does it cost to run?", string.Empty)],
            rows.BlogFaq);
        GccFaqEntry[] unanswered = [new("How long does a rollout take?", string.Empty), new("What does it cost to run?", string.Empty)];
        Assert.Equal(unanswered, lines.BlogFaq);
        Assert.Equal(unanswered, array.BlogFaq);
        Assert.Null(none.BlogFaq);
        // The questions alone are what a heading may be tagged as taken from, with or without an answer.
        Assert.Equal(["A pillar question?"], none.PaaQuestions);
    }

    [Fact]
    public void The_blog_faq_prompt_is_the_pillars_shape_on_a_blog_under_its_own_heading()
    {
        var request = Builder.BuildBlogFaqFromAnswersPrompt(
            Context(),
            new BlogMetadataDraft("A Blog Title", "Meta", ["ai"], ["A"]),
            GccFaqAnswers.Render(TwoAnswered));

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Write ONLY the \"Frequently Asked Questions\" FAQ section of a BlogPosting blog.", system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"Frequently Asked Questions\"", system, StringComparison.Ordinal);
        Assert.DoesNotContain("People Also Ask", system, StringComparison.Ordinal);
        Assert.Contains("Article title: A Blog Title", user, StringComparison.Ordinal);
        Assert.Contains("Q1: How long does a rollout take?", user, StringComparison.Ordinal);
        Assert.Contains("Q2: What does it cost to run?", user, StringComparison.Ordinal);
        Assert.Equal(3072, request.MaxOutputTokens);
    }

    /// <summary>
    /// 2026-10-13: the pillar's and the blog's questions are written from the answers the operator
    /// entered. From 2026-10-11 until then the request carried what a search of each partner's crawl
    /// found and told the writer to answer only where a passage answered.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void A_pillars_or_a_blogs_faq_prompt_hands_the_writer_each_question_with_its_answer(string page)
    {
        var answers = GccFaqAnswers.Render(TwoAnswered);
        var request = page == "pillar"
            ? Builder.BuildArticleFaqFromAnswersPrompt(
                Context(), new ArticleMetadataDraft("A Title", "Meta", ["ai"], ["A"]), answers)
            : Builder.BuildBlogFaqFromAnswersPrompt(
                Context(), new BlogMetadataDraft("A Title", "Meta", ["ai"], ["A"]), answers);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains(ContentPromptBuilder.OperatorAnswersInstruction, system, StringComparison.Ordinal);
        Assert.Contains("Each question is a child Section", system, StringComparison.Ordinal);
        Assert.Contains("paragraphs holds its answer", system, StringComparison.Ordinal);
        // An answer is as long as the one entered, not a fixed two to four sentences.
        Assert.DoesNotContain("2-4 sentence", system, StringComparison.Ordinal);
        // The same money rule every other call that states figures carries.
        Assert.Contains("US dollars", system, StringComparison.OrdinalIgnoreCase);
        // No passage, and no deciding which question can be answered.
        Assert.DoesNotContain("PARTNER EVIDENCE", system + user, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAVE THAT QUESTION OUT", system, StringComparison.Ordinal);

        Assert.Contains(ContentPromptBuilder.OperatorAnswersHeading, user, StringComparison.Ordinal);
        Assert.Contains(
            "Q1: How long does a rollout take?" + Environment.NewLine
            + "The operator's answer to Q1:" + Environment.NewLine
            + "Six weeks for a team of ten.",
            user,
            StringComparison.Ordinal);
        Assert.Contains(
            "The operator's answer to Q2:" + Environment.NewLine + "Nothing beyond the licence.", user, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillars_faq_prompt_from_questions_alone_is_written_as_it_always_was()
    {
        // The Workflow product's writer: it holds no answer to pass.
        var request = Builder.BuildArticleFaqSectionPrompt(
            Context(), new ArticleMetadataDraft("A Pillar", "Meta", ["ai"], ["A"]), ["How long does a rollout take?"], isRegeneration: false);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Write ONLY the \"People Also Ask\" FAQ section of a TechnicalArticle pillar.", system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"People Also Ask\"", system, StringComparison.Ordinal);
        Assert.Contains("Each question is a child Section", system, StringComparison.Ordinal);
        Assert.Contains("paragraphs holds a 2-4 sentence answer", system, StringComparison.Ordinal);
        Assert.DoesNotContain("OPERATOR'S ANSWER", system, StringComparison.Ordinal);
        Assert.Contains("Questions to answer:", user, StringComparison.Ordinal);
        Assert.Contains("  - Q1: How long does a rollout take?", user, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_faq_prompt_hands_the_writer_each_question_with_its_answer_and_the_briefs_voice()
    {
        var request = Builder.BuildToolFaqFromAnswersPrompt(
            Context() with { ToneOfVoice = "consultant_professional" },
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", null, "https://partner.test/widget"),
            GccFaqAnswers.Render(
            [
                new GccFaqEntry("Does it sync with QuickBooks Online?", "Yes: every payment is synced as it is made."),
                new GccFaqEntry("Does it fly?", "No."),
            ]));

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains(ContentPromptBuilder.OperatorAnswersInstruction, system, StringComparison.Ordinal);
        Assert.Contains("heading is exactly \"Frequently Asked Questions\"", system, StringComparison.Ordinal);
        Assert.Contains("paragraphs holds its answer", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTNER EVIDENCE", system + user, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAVE THAT QUESTION OUT", system, StringComparison.Ordinal);
        Assert.Contains(ContentPromptBuilder.OperatorAnswersHeading, user, StringComparison.Ordinal);
        Assert.Contains("Q1: Does it sync with QuickBooks Online?", user, StringComparison.Ordinal);
        Assert.Contains("Yes: every payment is synced as it is made.", user, StringComparison.Ordinal);
        Assert.Contains("The operator's answer to Q2:" + Environment.NewLine + "No.", user, StringComparison.Ordinal);
        Assert.Contains("Page topic: Partner Widget", user, StringComparison.Ordinal);
        Assert.DoesNotContain("Pillar topic", user, StringComparison.Ordinal);
        // The brief sets the voice and is not a source: every fact is the operator's answer.
        Assert.Contains("every fact comes from the operator's answers below", user, StringComparison.Ordinal);
        Assert.Equal(3072, request.MaxOutputTokens);
    }
}
