using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// Two rules that existed in one of the four outline prompts, or in none of them.
///
/// <para>
/// The Tools-H2 ban was written only into the pillar's outline prompt, so nothing ever told a blog
/// not to write one -- which is why "Choosing the Right AI Tools for Your Accounts Payable Needs"
/// turned up on blogs (Jeff, 2026-09-28).
/// </para>
///
/// <para>
/// And GcwSeoAnalyzer has always scored keyword-in-lede and keyword-in-heading, while the only
/// placement instruction anywhere was "metaDescription must include the target keyword". Two of its
/// five checks marked drafts down for not doing something nobody had asked for.
/// </para>
/// </summary>
public class OutlinePromptRulesTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "Automated Data Entry & Processing",
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

    private static string Rendered(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    private static string Outline(string which)
    {
        var builder = new ContentPromptBuilder();
        return which switch
        {
            "pillar" => Rendered(builder.BuildArticleMetadataPrompt(Context())),
            "blog" => Rendered(builder.BuildStandaloneBlogMetadataPrompt(Context())),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown outline prompt"),
        };
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void Neither_outline_may_carry_a_tools_heading(string which)
    {
        Assert.Contains("Do not include a Tools H2", Outline(which), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_outline_asks_for_the_keyword_where_the_score_looks_for_it(string which)
    {
        var rendered = Outline(which);

        Assert.Contains("KEYWORD PLACEMENT", rendered, StringComparison.Ordinal);
        Assert.Contains("appears in at least one H2", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void And_says_not_to_stuff_it(string which)
    {
        // The density check fails a draft that took "put the keyword in a heading" as licence to put
        // it in all of them, so the instruction that creates that risk has to close it.
        Assert.Contains("Not in every heading", Outline(which), StringComparison.Ordinal);
    }

    [Fact]
    public void The_lede_prompt_is_the_one_told_to_put_the_keyword_in_the_lede()
    {
        // It was in the outline prompts, which plan the piece and do not write the lede. So
        // "keyword in lede" failed on a draft whose lede prompt had never been told.
        var builder = new ContentPromptBuilder();
        var rendered = string.Join("\n", builder
            .BuildStandaloneBlogLedePrompt(Context(), new BlogMetadataDraft("T", "M", ["ai"], ["One"]))
            .Messages.Select(m => m.Content));

        Assert.Contains("the opening paragraph contains", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automated Data Entry & Processing", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_body_prompt_is_given_a_frequency_to_aim_at_not_just_a_warning()
    {
        // "Not repeated to hit a count" was the whole guidance and the model obeyed it exactly: one
        // mention in 1,132 words, 0.09% against a 0.4% floor.
        var builder = new ContentPromptBuilder();
        var rendered = string.Join("\n", builder
            .BuildStandaloneBlogBodyPrompt(Context(), new BlogMetadataDraft("T", "M", ["ai"], ["One"]))
            .Messages.Select(m => m.Content));

        Assert.Contains("KEYWORD FREQUENCY", rendered, StringComparison.Ordinal);
        Assert.Contains("about once every 200 words", rendered, StringComparison.Ordinal);
        Assert.Contains("one mention in a long piece fails it", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_length_instruction_carries_the_per_section_arithmetic()
    {
        // A total alone is satisfiable by one long section and four thin ones, which is how a piece
        // asked for 2,000 words came back at 1,132.
        var builder = new ContentPromptBuilder();
        var rendered = string.Join("\n", builder
            .BuildStandaloneBlogBodyPrompt(Context(), new BlogMetadataDraft("T", "M", ["ai"], ["One"]))
            .Messages.Select(m => m.Content));

        Assert.Contains("words each", rendered, StringComparison.Ordinal);
        Assert.Contains("is a floor", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_keyword_named_is_the_one_the_scorer_will_look_for(string which)
    {
        // Not the context half: a topic is "Accounts Payable: Automated Data Entry & Processing" and
        // the keyword is the second half, which is what GccTargetKeyword hands the scorer.
        Assert.Contains("\"Automated Data Entry & Processing\" appears", Outline(which), StringComparison.Ordinal);
    }
}
