using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// A pillar carries no Tools H2. Jeff, 2026-09-26: "shouldn't be a dedicated Tool section" -- tools
/// are named in running prose, each linked to its own page, and a standalone listing is Generate
/// Tools' content, not the pillar's.
///
/// <para>
/// The rule was already the intent and had no test, which is how a whole second implementation of
/// the opposite -- a platform-list call, a per-platform h3 builder, an extractor that read the tools
/// back out of the section -- survived unreferenced long enough to be reported as a live defect.
/// These two assertions are what stops that: the prompt forbids the section, and the plan check
/// rejects an outline that smuggles one in.
/// </para>
/// </summary>
public class PillarHasNoToolsSectionTests
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
        Provider: LlmProviderType.OpenAi,
        KnownCrawlTools: [new KnownCrawlTool("Tipalti", null), new KnownCrawlTool("Melio", null)]);

    [Fact]
    public void The_pillar_outline_prompt_forbids_a_tools_heading()
    {
        var prompt = new ContentPromptBuilder().BuildArticleMetadataPrompt(Context());
        var rendered = string.Join("\n", prompt.Messages.Select(m => m.Content));

        Assert.Contains("Do not include a Tools H2", rendered, StringComparison.Ordinal);
        Assert.Contains("belong in body sentences later, not as outline headings", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Top Tools for Accounts Payable")]
    [InlineData("AI Tools")]
    [InlineData("The Right Tool for the Job")]
    public void An_outline_heading_that_lists_tools_is_rejected(string heading)
    {
        Assert.True(PillarSectionClassifier.IsToolsListingHeading(heading));
    }

    [Theory]
    [InlineData("Common Challenges and Solutions")]
    [InlineData("Choosing a Platform")]
    [InlineData("Benefits of Automation")]
    public void A_heading_that_merely_sounds_adjacent_is_not_rejected(string heading)
    {
        // The looser predicate that matched "platform", "solution" and "vendor" went with the
        // section it existed to find. This check rejects a plan outright, so a false positive blocks
        // valid work -- it once flagged "Common Challenges and Solutions" on the word "solution".
        Assert.False(PillarSectionClassifier.IsToolsListingHeading(heading));
    }
}
