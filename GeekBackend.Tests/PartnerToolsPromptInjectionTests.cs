using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests;

/// <summary>
/// Runtime proof + regression: pillar ArticleSection omits CrawledParagraphs; partner tools must
/// reach WRITE via WritingNotes. Blog site-copy must not Take(5)-drop partner blocks.
/// </summary>
public class PartnerToolsPromptInjectionTests
{
    private static ProjectGenerationContext Ctx(List<string> crawledParagraphs, string? writingNotes = null) =>
        new(
            ProjectName: "kw",
            ProjectUrl: "https://example.com",
            TargetKeyword: "kw",
            Department: "marketing",
            SiteName: "Example",
            DetectedTone: "t",
            DetectedFocus: "f",
            CrawledHeadings: [],
            CrawledParagraphs: crawledParagraphs,
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: [],
            PublisherName: "Example",
            PublisherLogoUrl: "",
            AuthorName: "a",
            ArticleBaseUrl: "https://example.com",
            BlogBaseUrl: "https://example.com/blog",
            ToolBaseUrl: "https://example.com/tools",
            ImplementerPositioning: "p",
            Provider: LlmProviderType.OpenAi,
            WritingNotes: writingNotes);

    private static List<string> FluffThenPartner() =>
    [
        "Company: Example",
        "About the company: we help.",
        "Positioning: implementers.",
        "Services/features: a, b, c",
        "Topics this company is known for: x",
        "MUST MENTION partner tools (required): Intercom <https://www.intercom.com> | Tidio <https://www.tidio.com>",
        "PARTNER PAGE RESEARCH (fetched destination pages):",
        "[Intercom] (https://www.intercom.com)",
        "- Intercom helps teams reply faster.",
    ];

    [Fact]
    public void ArticleSection_brief_omits_CrawledParagraphs_partner_must_mention()
    {
        var brief = ResearchBriefBuilder.Build(Ctx(FluffThenPartner()), ResearchBriefPhase.ArticleSection);

        Assert.DoesNotContain("MUST MENTION partner tools", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTNER PAGE RESEARCH", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void ArticleSection_system_path_keeps_partner_tools_when_in_WritingNotes()
    {
        var notes = "MUST MENTION partner tools (required): Intercom <https://www.intercom.com>";
        var ctx = Ctx([], notes);
        var builder = new ContentPromptBuilder();
        var req = builder.BuildArticleSectionPrompt(
            ctx,
            new ArticleMetadataDraft("T", "M", ["kw"], ["S1"]),
            "S1",
            0,
            1,
            ["S1"],
            isRegeneration: false);

        var system = string.Join("\n", req.Messages
            .Where(m => m.Role == GeekAPI.Services.Workflow.Providers.ChatRole.System)
            .Select(m => m.Content));
        Assert.Contains("MUST MENTION partner tools", system, StringComparison.Ordinal);
        Assert.Contains("Intercom", system, StringComparison.Ordinal);
    }

    [Fact]
    public void BlogSection_keeps_partner_must_mention_past_index_4()
    {
        var brief = ResearchBriefBuilder.Build(Ctx(FluffThenPartner()), ResearchBriefPhase.BlogSection);

        Assert.Contains("MUST MENTION partner tools", brief, StringComparison.Ordinal);
        Assert.Contains("PARTNER PAGE RESEARCH", brief, StringComparison.Ordinal);
        Assert.Contains("Intercom", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectSiteCopyParagraphs_prefers_partner_block_over_raw_Take5()
    {
        var selected = ResearchBriefBuilder.SelectSiteCopyParagraphs(FluffThenPartner());
        Assert.Contains(selected, p => p.Contains("MUST MENTION partner tools", StringComparison.Ordinal));
        Assert.Contains(selected, p => p.Contains("PARTNER PAGE RESEARCH", StringComparison.Ordinal));
    }
}
