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
        Assert.Contains("appears in the opening paragraph and in at least one H2", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void And_says_not_to_stuff_it(string which)
    {
        // The density check fails a draft that took "put the keyword in a heading" as licence to put
        // it in all of them, so the instruction that creates that risk has to close it.
        var rendered = Outline(which);

        Assert.Contains("Not in every heading", rendered, StringComparison.Ordinal);
        Assert.Contains("not repeated to hit a count", rendered, StringComparison.Ordinal);
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
