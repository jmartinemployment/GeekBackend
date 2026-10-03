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
/// A blog generate failed with <c>unknown ledeType 'problem_solution'</c> (Jeff, 2026-10-03).
/// </summary>
/// <remarks>
/// <para>
/// <c>problem_solution</c> is an <b>angle</b>, not a lede type. The lede guidance printed a table of all
/// four angles as bare left-hand tokens — <c>problem_solution → prefers anecdotal, ...</c> — one line
/// above "Pick ONE ledeType from the 12", so the model answered with the key instead of the value and
/// <c>ParseLedeTypeStrict</c> refused it. Refusing is correct; being asked an ambiguous question is not.
/// </para>
/// <para>
/// Three of those four rows could never apply: <c>angle</c> is a required brief field, so exactly one is
/// live. The others were noise whose only effect was to offer a wrong answer.
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

    /// <summary>The four angle tokens that must never be offered where a ledeType is asked for.</summary>
    public static TheoryData<string> Angles() => new()
    {
        "problem_solution", "comparative", "case_study_data", "ultimate_guide",
    };

    [Theory]
    [MemberData(nameof(Angles))]
    public void No_angle_name_is_ever_offered_as_a_lede_type(string angle)
    {
        // The exact failure: the angle appeared as a selectable token beside the ledeType ask. It may
        // still be described in prose by DescribeAngle -- that is the angle doing its own job -- but it
        // must not appear in the "prefer one of" list the model picks a ledeType from.
        var prompt = BlogLede(angle);

        Assert.DoesNotContain($"{angle} →", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Lede guidance by angle", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Angles))]
    public void Only_this_briefs_angle_shapes_the_lede_and_the_other_three_are_absent(string angle)
    {
        var prompt = BlogLede(angle);

        Assert.Contains("For this brief's angle, prefer one of:", prompt, StringComparison.Ordinal);

        // Mutation guard: restore the four-row table and this fails, because a row for an angle this
        // brief does not have would be present.
        foreach (var other in new[] { "problem_solution", "comparative", "case_study_data", "ultimate_guide" })
        {
            if (other == angle) continue;
            Assert.DoesNotContain(other, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_prompt_says_outright_that_brief_values_are_not_lede_types()
    {
        // Belt and braces, and cheap: the guidance still reads "<brief value> -> <ledeTypes>" for
        // audience, intent and funnel stage, so the same echo is available there.
        var prompt = BlogLede("problem_solution");

        Assert.Contains("brief values, NOT", prompt, StringComparison.Ordinal);
        Assert.Contains("NEVER return one of them as ledeType", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_solution_prefers_the_pain_first_types()
    {
        // The guidance itself has to survive the rewrite -- dropping the table must not drop the advice.
        var prompt = BlogLede("problem_solution");

        Assert.Contains("anecdotal, sceneSetting, directAddress, question", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognised_angle_offers_no_preference_rather_than_all_four()
    {
        // Fail closed: an angle nothing maps gets no lede steer, not every steer at once.
        var prompt = BlogLede("something_new");

        Assert.DoesNotContain("For this brief's angle, prefer one of:", prompt, StringComparison.Ordinal);
        Assert.Contains("Pick ONE ledeType", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillar_lede_gets_the_same_treatment()
    {
        // Same helper, so the same fix -- asserted because the reported failure was blog and a fix that
        // only covered blog would leave pillar one generate away from the same refusal.
        var prompt = PillarLede("problem_solution");

        Assert.DoesNotContain("comparative", prompt, StringComparison.Ordinal);
        Assert.Contains("brief values, NOT", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_twelve_values_the_prompt_offers_are_exactly_the_twelve_the_guard_accepts()
    {
        // Prompt and guard are one rule in two places. ParseLedeTypeStrict rejects anything outside
        // Enum.GetNames<LedeType>(), while the JSON contract hand-lists the names in camelCase -- so
        // adding a thirteenth type, or renaming one, silently makes the prompt offer a value the guard
        // refuses. This is the drift that produced a refusal nobody could act on.
        var prompt = BlogLede("problem_solution");

        foreach (var name in Enum.GetNames<LedeType>())
        {
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            Assert.Contains($"\"{camel}\"", prompt, StringComparison.Ordinal);
        }
    }
}
