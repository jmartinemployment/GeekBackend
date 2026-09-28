using GeekAPI.Services.ContentCreatorV2.ContentTypes;
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

    private static string Lede()
    {
        var builder = new ContentPromptBuilder();
        return Rendered(builder.BuildStandaloneBlogLedePrompt(
            Context(), new BlogMetadataDraft("T", "M", ["ai"], ["One"])));
    }

    private static string Body(string which)
    {
        var builder = new ContentPromptBuilder();
        return which switch
        {
            "blog" => Rendered(builder.BuildStandaloneBlogBodyPrompt(
                Context(), new BlogMetadataDraft("T", "M", ["ai"], ["One"]))),
            "pillar" => Rendered(builder.BuildArticleSectionBatchPrompt(
                Context(),
                new ArticleMetadataDraft("T", "M", ["ai"], ["One"]),
                slots: [SectionSlot.Assigned("One")],
                fullOutline: [SectionSlot.Assigned("One")],
                isRegeneration: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown body prompt"),
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
    public void The_outline_asks_for_the_keyword_in_a_heading(string which)
    {
        var rendered = Outline(which);

        Assert.Contains("at least one H2 contains", rendered, StringComparison.Ordinal);
        Assert.Contains("Automated Data Entry & Processing", rendered, StringComparison.Ordinal);
        // The other half of the same rule, because "put it in a heading" read as licence to put it
        // in all of them and the density check fails that.
        Assert.Contains("not more than two", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lede_prompt_owns_the_lede_rules()
    {
        // These were in the outline prompts, which plan the piece and do not write the lede, so
        // "keyword in lede" failed on a draft whose lede prompt had never been told.
        var rendered = Lede();

        Assert.Contains("the opening contains", rendered, StringComparison.Ordinal);
        Assert.Contains("Automated Data Entry & Processing", rendered, StringComparison.Ordinal);
        Assert.Contains("within its first hundred words", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar", 3000, 3)]
    [InlineData("blog", 1800, 3)]
    public void The_body_is_given_the_floor_the_scorer_actually_uses(string which, int minWords, int minSections)
    {
        // The whole point of the rewrite. ContentLengthTargets drove the prompt and
        // GccV2LongFormTypes.GetSeoLengthRules drove the score, and they disagreed -- the blog
        // prompt asked for 2,000 against a scored floor of 1,800, the tool prompt for 3,000 against
        // 1,500. One source now, the scorer's own.
        var expected = GccV2LongFormTypes.GetSeoLengthRules(which);
        Assert.Equal((minWords, minSections, true), expected);

        var rendered = Body(which);
        Assert.Contains($"{minWords:N0} words is the floor", rendered, StringComparison.Ordinal);
        Assert.Contains($"at least {minSections} top-level sections", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_body_is_given_a_frequency_to_aim_at(string which)
    {
        // "Not repeated to hit a count" was the whole guidance and the model obeyed it exactly: one
        // mention in 1,132 words, 0.09% against a 0.4% floor.
        var rendered = Body(which);

        Assert.Contains("KEYWORD FREQUENCY", rendered, StringComparison.Ordinal);
        Assert.Contains("roughly once every 200 words", rendered, StringComparison.Ordinal);
        Assert.Contains("One mention in a long piece fails this", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_body_is_told_to_answer_the_heading_and_to_use_lists_where_they_extract(string which)
    {
        // From seo-aeo-best-practices: lead with the answer, and structured content is extracted
        // more reliably than the same material as prose.
        var rendered = Body(which);

        Assert.Contains("DIRECT ANSWERS", rendered, StringComparison.Ordinal);
        Assert.Contains("answers its own heading in its first two sentences", rendered, StringComparison.Ordinal);
        Assert.Contains("extracted more reliably", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_per_section_arithmetic_is_stated_not_just_the_total(string which)
    {
        // A total alone is satisfiable by one long section and several thin ones, which is how a
        // piece asked for a floor came back at half of it.
        Assert.Contains("words each", Body(which), StringComparison.Ordinal);
    }
}
