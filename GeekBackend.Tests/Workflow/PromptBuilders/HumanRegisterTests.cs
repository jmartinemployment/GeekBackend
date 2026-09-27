using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The register and the banned vocabulary, asserted on the rendered prompt.
///
/// <para>
/// Jeff, 2026-09-27, on a finished blog: reported as 100% AI-detected. The rhythm half of this
/// instruction already existed and clearly was not enough on its own -- what the draft actually read
/// like was a formal opener, passive constructions and the buzzwords a model reaches for when it has
/// nothing specific to say. His brief is now in the prompt; these hold it there.
/// </para>
/// </summary>
public class HumanRegisterTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "accounts payable automation",
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
        Provider: LlmProviderType.OpenAi);

    private static string BlogPrompt() =>
        string.Join("\n", new ContentPromptBuilder()
            .BuildStandaloneBlogBodyPrompt(Context(), new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]))
            .Messages.Select(m => m.Content));

    private static string PillarPrompt() =>
        string.Join("\n", new ContentPromptBuilder()
            .BuildArticleSectionBatchPrompt(
                Context(),
                new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview"]),
                slots: [SectionSlot.Assigned("Overview")],
                fullOutline: [SectionSlot.Assigned("Overview")],
                isRegeneration: false)
            .Messages.Select(m => m.Content));

    [Theory]
    [InlineData("delve")]
    [InlineData("testament")]
    [InlineData("tapestry")]
    [InlineData("beacon")]
    [InlineData("realm")]
    [InlineData("dynamic")]
    [InlineData("pivotal")]
    [InlineData("navigating")]
    public void Every_banned_word_is_named_in_the_blog_prompt(string word)
    {
        Assert.Contains(word, BlogPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_register_is_a_person_talking_to_a_colleague()
    {
        var blog = BlogPrompt();

        Assert.Contains("explaining this to a colleague over coffee", blog, StringComparison.Ordinal);
        Assert.Contains("Active voice", blog, StringComparison.Ordinal);
    }

    [Fact]
    public void No_formal_opener_and_no_wrap_up()
    {
        // "In the dimly lit back office of a bustling local business..." then a closing paragraph
        // restating the piece is the shape that was reported.
        var blog = BlogPrompt();

        Assert.Contains("No formal opener and no wrap-up", blog, StringComparison.Ordinal);
        Assert.Contains("Start talking, and stop when you are done", blog, StringComparison.Ordinal);
    }

    [Fact]
    public void Consequently_joins_the_banned_transitions()
    {
        Assert.Contains("Consequently", BlogPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void Three_adjectives_in_a_row_are_banned_with_the_other_cadence_tricks()
    {
        Assert.Contains("no three adjectives in a row", BlogPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillar_gets_the_same_register_as_the_blog()
    {
        // One constant, twelve call sites. A register that held on blog and not on pillar would be
        // the drift this is written once to avoid.
        var pillar = PillarPrompt();

        Assert.Contains("explaining this to a colleague over coffee", pillar, StringComparison.Ordinal);
        Assert.Contains("delve", pillar, StringComparison.Ordinal);
    }
}
