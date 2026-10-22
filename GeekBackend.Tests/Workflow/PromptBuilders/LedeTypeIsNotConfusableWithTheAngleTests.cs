using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// Every opening is a Question lede, and the opening request offers the writer no other type.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-22 the request listed twelve lede types and asked the writer to pick one against the
/// brief. Two things went wrong with that. A blog generate failed with <c>unknown ledeType
/// 'problem_solution'</c> (Jeff, 2026-10-03): the angle was printed as a bare token beside the ask and the
/// model returned the key. And the pick was the writer's: geekatyourspot's GEO and SEO audit of
/// 2026-10-20 counted 40 pages opening "Picture" or "Imagine". Jeff, 2026-10-22: the Question Lede.
/// </para>
/// <para>
/// The lede request attaches no JSON schema, so nothing constrains the field provider-side and this
/// prompt is the whole defence. That is why it is tested here rather than left to the guard.
/// </para>
/// </remarks>
public class LedeTypeIsNotConfusableWithTheAngleTests
{
    private static ProjectGenerationContext Context(string? angle) => new(
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
        Provider: LlmProviderType.OpenAi,
        ContentAngle: angle,
        AudienceSegment: "smb",
        PrimaryIntent: "commercial");

    private static string Prompt(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    private static string BlogLede(string? angle) =>
        Prompt(new ContentPromptBuilder().BuildStandaloneBlogLedePrompt(
            Context(angle), new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"])));

    private static string PillarLede(string? angle) =>
        Prompt(new ContentPromptBuilder().BuildPillarLedePrompt(
            Context(angle),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview"]),
            ledeHeading: "Overview",
            ledeIndex: 0,
            totalSections: 2,
            fullOutline: [SectionSlot.Assigned("Overview"), SectionSlot.Assigned("Details")],
            isRegeneration: false));

    /// <summary>The four angle tokens, which must never be offered where a ledeType is asked for.</summary>
    private static readonly string[] AngleTokens = ["problem_solution", "comparative", "case_study_data", "ultimate_guide"];

    public static TheoryData<string> Angles() => new(AngleTokens);

    [Theory]
    [MemberData(nameof(Angles))]
    public void No_angle_name_is_ever_offered_as_a_lede_type(string angle)
    {
        // The angle is described in prose by DescribeAngle -- that is the angle doing its own job -- and
        // never appears as a token the model could return as the ledeType.
        var prompt = BlogLede(angle);

        Assert.DoesNotContain($"{angle} →", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Lede guidance by angle", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("prefer one of", prompt, StringComparison.Ordinal);
        foreach (var other in AngleTokens.Where(other => other != angle))
        {
            Assert.DoesNotContain(other, prompt, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("blog")]
    [InlineData("pillar")]
    public void The_opening_is_a_question_lede_and_no_other_type_is_offered(string page)
    {
        var prompt = page == "blog" ? BlogLede("problem_solution") : PillarLede("problem_solution");

        Assert.Contains(ContentPromptBuilder.QuestionLedeInstruction, prompt, StringComparison.Ordinal);
        Assert.Contains("\"ledeType\": \"question\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Pick ONE ledeType", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Lede types (pick", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("brief values, NOT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Do NOT start with \"How\"", prompt, StringComparison.Ordinal);
        // "quote" is also a paragraph type in the JSON shape, so it is checked as the menu wrote it.
        Assert.DoesNotContain("\"quote\"|\"wordplay\"", prompt, StringComparison.Ordinal);
        foreach (var name in Enum.GetNames<LedeType>())
        {
            if (name is nameof(LedeType.Question) or nameof(LedeType.Quote)) continue;
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            Assert.DoesNotContain($"\"{camel}\"", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_one_value_the_prompt_asks_for_is_one_the_parser_accepts()
    {
        // Prompt and parser are one rule in two places: ParseLedeTypeStrict accepts the enum's names, and
        // the contract names "question". A rename of the enum member would make the prompt ask for a value
        // the parser refuses.
        Assert.True(Enum.TryParse<LedeType>("question", ignoreCase: true, out var parsed));
        Assert.Equal(LedeType.Question, parsed);
        Assert.Contains("\"question\"", BlogLede("comparative"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_brief_still_shapes_the_opening_in_words()
    {
        var prompt = BlogLede("comparative");

        Assert.Contains("The brief shapes the question, the answer and the turn:", prompt, StringComparison.Ordinal);
        Assert.Contains("Comparative", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Angle: comparative", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognised_angle_is_passed_through_and_offers_no_type()
    {
        var prompt = BlogLede("something_new");

        Assert.Contains("something_new", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("prefer one of", prompt, StringComparison.Ordinal);
        Assert.Contains(ContentPromptBuilder.QuestionLedeInstruction, prompt, StringComparison.Ordinal);
    }
}
