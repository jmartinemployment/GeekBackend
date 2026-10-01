using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The no-tools-section rule has to reach the call that plans the headings, not only the one that
/// writes them.
/// </summary>
/// <remarks>
/// <para>
/// A planned outline arrives at the body writer as assigned slots. So when the metadata call planned
/// <i>"Choosing the Right Tools for Your Accounts Payable Needs"</i>, the body writer was told never to
/// write a tools section and handed one as its heading; it followed the outline, and the retry
/// re-wrote against the same outline, so no retry could succeed. A live generate refused on exactly
/// that.
/// </para>
/// <para>
/// The outline prompts did carry a ban — <i>"Do not include a Tools H2"</i> — in three
/// separately-worded copies, all narrower than the rule the body prompt and the guard enforce. None of
/// them covered a selection-framing heading. These tests pin the planning stage against the same
/// constant, so the three cannot drift into three rules again.
/// </para>
/// </remarks>
public class NoToolsSectionReachesTheOutlineTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "Accounts Payable Automation",
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
        MatchedUseCase: null);

    public static TheoryData<string> OutlinePlanningPrompts() =>
        new("standalone-blog", "companion-blog", "pillar");

    [Theory]
    [MemberData(nameof(OutlinePlanningPrompts))]
    public void Every_prompt_that_plans_headings_names_the_shapes_it_must_not_plan(string which)
    {
        var builder = new ContentPromptBuilder();
        var context = Context();
        var request = which switch
        {
            "standalone-blog" => builder.BuildStandaloneBlogMetadataPrompt(context),
            "companion-blog" => builder.BuildBlogMetadataPrompt(
                context,
                new ArticleDraft("Pillar title", "Meta", new ContentDocument(null, []), ["AP"], 1200, [])),
            _ => builder.BuildArticleMetadataPrompt(context),
        };

        var prompt = string.Join("\n", request.Messages.Select(m => m.Content));

        // The exact shape the live refusal was about. "Do not include a Tools H2" never covered it.
        Assert.Contains("Choosing the Right Tools", prompt, StringComparison.Ordinal);
        Assert.Contains("NO TOOLS SECTION", prompt, StringComparison.Ordinal);

        // And said as an instruction about the outline being planned, since the body writer cannot
        // decline a heading it is handed.
        Assert.Contains("sectionOutline heading", prompt, StringComparison.Ordinal);
    }
}
