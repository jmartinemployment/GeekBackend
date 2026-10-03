using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// Reader-facing prose runs on the writing model; only work nobody reads is Utility.
/// </summary>
/// <remarks>
/// <c>LlmTaskClass.Utility</c> resolves to <c>UtilityModel</c>, which is configured to a cheap model.
/// <c>BuildToolFaqSectionPrompt</c> was marked Utility with the comment "no prose a reader reads", so
/// every tool page's FAQ was written by the cheap model while the rest of the page used the writing
/// model — a quality seam nobody chose, and invisible in the output because both render the same.
/// </remarks>
public class UtilityIsOnlyForWorkNobodyReadsTests
{
    private static readonly ContentPromptBuilder Builder = new();

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "automated approval workflows",
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
    public void The_tool_faq_is_written_by_the_writing_model()
    {
        // The FAQ ships on the page under its own headings. A reader cannot tell which model wrote which
        // section, which is exactly why this had gone unnoticed.
        var request = Builder.BuildToolFaqSectionPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", "A widget."),
            // An empty bank is fine: the task class is a property of the prompt, not of its content.
            []);

        Assert.Equal(LlmTaskClass.Writing, request.TaskClass);
    }

    [Fact]
    public void Image_prompts_stay_utility_because_nobody_reads_them()
    {
        // The other side of the line: an image prompt is an instruction to a generator, never published.
        // If this ever flips to Writing, the distinction has been lost rather than refined.
        var request = Builder.BuildStandaloneImagePrompt("accounts payable", "notes", null);

        Assert.Equal(LlmTaskClass.Utility, request.TaskClass);
    }

    [Fact]
    public void Writing_is_the_default_so_a_new_prompt_cannot_land_on_the_cheap_model_by_omission()
    {
        // The safe direction for a default: forgetting to classify a new prompt costs money, not quality.
        Assert.Equal(
            LlmTaskClass.Writing,
            new ChatCompletionRequest(Messages: [new(ChatRole.System, "x")]).TaskClass);
    }
}
