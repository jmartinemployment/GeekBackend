using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// No content type lets the model choose what its sections <b>are</b>.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that makes the no-tools-section rule enforced rather than policed. Jeff, four times,
/// most recently 2026-10-02: <i>"Pillar &amp; Blog are not to contain a tool section, period"</i>,
/// <i>"Tools have no headings within Pillar &amp; Blogs"</i>.
/// </para>
/// <para>
/// It kept recurring because the enforcement was a heading detector, and a detector can only ever catch a
/// lexical subset — it needed the word <i>tools</i> plus an enumerating phrase, so a heading named
/// <c>Dext</c> passed it. Every way a model can name a product is another case; there is no finite list.
/// Jeff: <i>"this by definition should not exist, FindToolsHeadings."</i>
/// </para>
/// <para>
/// A section is about a problem or its solution, never about a product. That is enforceable as a closed
/// set: if no slot carries heading text, the model never chooses the section set, and "Best Tools for X"
/// is not among the answers it can give. Blog was the only type that did — which is why every
/// tools-section refusal in three live runs was the blog, and never the pillar or the tool page.
/// </para>
/// </remarks>
public class SectionPlansAreOwnedByCodeTests
{
    private static ProjectGenerationContext Context(string? angle = "problem_solution") => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        // The keyword, not the whole Topic -- see ProjectGenerationContext.TargetKeyword. The fixture
        // used to carry "Accounts Payable: Automated Data Entry & Processing" and relied on BlogPrompts
        // splitting it again, which is the double-split that made the keyword scorer and the outline
        // disagree. A context never holds a Topic, so a fixture that holds one tests a state that cannot
        // occur. The producer-side half is pinned end to end by WriterIsToldTheKeywordNotTheTopicTests.
        TargetKeyword: "Automated Data Entry & Processing",
        Department: "accounting",
        SiteName: "Acme",
        DetectedTone: "Professional",
        DetectedFocus: "AP",
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Acme",
        PublisherLogoUrl: "https://acme.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://acme.test",
        BlogBaseUrl: "https://acme.test/blog",
        ToolBaseUrl: "https://acme.test/tools",
        ImplementerPositioning: "AI implementer",
        Provider: LlmProviderType.OpenAi,
        UseExactKeywordAsTitle: false,
        DesiredHeadings: null,
        MatchedUseCase: null)
    {
        ContentAngle = angle,
    };

    private static ContentTypePromptContext Ctx(string? angle = "problem_solution") => new(
        Context: Context(angle),
        App: new SoftwareApplicationDescriptor("Dext", "AP automation"),
        BlogMetadata: new BlogMetadataDraft(
            "A title", "A meta description", ["AP"],
            // Deliberately poisoned: if any type still reads planned headings, this one is a tools listing
            // and the test below fails.
            ["Best Tools for Accounts Payable", "Choosing the Right Tools"]));

    private static IContentTypePrompts Type(string key) =>
        TestContentTypePrompts.Registry().Find(key)
        ?? throw new InvalidOperationException($"no prompt set for '{key}'");

    public static TheoryData<string> LiveTypes() => new("pillar", "blog", "tool");

    [Theory]
    [MemberData(nameof(LiveTypes))]
    public void No_live_type_takes_a_heading_from_the_model(string key)
    {
        var type = Type(key);

        var outline = type.OutlineFor(Ctx());

        Assert.NotEmpty(outline);
        Assert.All(outline, slot => Assert.True(
            slot.WritesItsOwnHeading,
            $"{key} slot carries heading text \"{slot.Heading}\" — the model must not choose what the "
            + "sections are, or a tools listing becomes a valid answer."));
    }

    [Theory]
    [MemberData(nameof(LiveTypes))]
    public void No_obligation_asks_for_a_tools_listing(string key)
    {
        var type = Type(key);

        foreach (var slot in type.OutlineFor(Ctx()))
        {
            var covers = slot.Covers ?? string.Empty;
            // The shapes the prompt names: an enumeration, or a selection. An obligation that mentions the
            // tools in passing is fine and expected -- Stage 2 requires it -- so this is about a section
            // whose JOB is the list.
            Assert.DoesNotContain("list of tools", covers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("best tools", covers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("choosing the right", covers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("top tools", covers, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_poisoned_planned_outline_reaches_nothing()
    {
        // The direct statement of the fix: a metadata call returning two tools listings changes no type's
        // section plan, because no type reads it.
        foreach (var key in new[] { "pillar", "blog", "tool" })
        {
            var outline = Type(key).OutlineFor(Ctx());
            Assert.DoesNotContain(
                outline.Select(sl => sl.Label),
                label => label.Contains("Best Tools", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("problem_solution")]
    [InlineData("comparative")]
    [InlineData("case_study_data")]
    [InlineData("ultimate_guide")]
    [InlineData(null)]
    public void The_blog_opening_is_shaped_by_the_angle(string? angle)
    {
        var outline = Type("blog").OutlineFor(Ctx(angle));

        Assert.True(outline.Count >= 5, $"blog planned {outline.Count} sections; the minimum is 5.");
        Assert.False(string.IsNullOrWhiteSpace(outline[0].Covers));
    }

    [Fact]
    public void The_blog_problem_is_the_manual_form_of_the_keyword()
    {
        // Topic is "descriptor: keyword" and the keyword names the SOLUTION, so the problem is doing it by
        // hand. The obligations say so, and they carry the keyword rather than the whole Topic.
        var outline = Type("blog").OutlineFor(Ctx());
        var covers = string.Join(" ", outline.Select(sl => sl.Covers));

        Assert.Contains("Automated Data Entry & Processing", covers, StringComparison.Ordinal);
        // A guard against a descriptor being interpolated back in, not a test of the split: the context
        // no longer carries one to split. The split itself is WriterIsToldTheKeywordNotTheTopicTests'.
        Assert.DoesNotContain("Accounts Payable:", covers, StringComparison.Ordinal);
        Assert.Contains("manual", covers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_blog_requires_the_tools_in_its_solution_prose()
    {
        // The obligation half. A ban alone produced a page with no tools at all, which is worse than the
        // section -- so a solution obligation says to name them there, in prose, linked.
        var covers = string.Join(
            " ",
            Type("blog").OutlineFor(Ctx())
                .Select(sl => $"{sl.Covers} {sl.Guidance}"));

        Assert.Contains("partner tools", covers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in the prose", covers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never a heading", covers, StringComparison.OrdinalIgnoreCase);
    }
}
