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
    private const string PartnerToolsLine =
        "Partner tools for this use case (required): BotPenguin <https://botpenguin.com/> | ManyChat <https://manychat.com/>";

    private const string PartnerExcerptsLine =
        "PARTNER PAGE EXCERPTS (fetched destination pages for weave — when discussing a tool in a paragraph:";

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
        PartnerToolsLine,
        PartnerExcerptsLine,
        "[BotPenguin] (https://botpenguin.com/)",
        "- BotPenguin helps teams reply faster.",
    ];

    [Fact]
    public void ArticleSection_brief_omits_CrawledParagraphs_partner_block()
    {
        var brief = ResearchBriefBuilder.Build(Ctx(FluffThenPartner()), ResearchBriefPhase.ArticleSection);

        Assert.DoesNotContain("Partner tools for this use case", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTNER PAGE EXCERPTS", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("allowlist", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArticleSection_system_path_keeps_partner_tools_when_in_WritingNotes()
    {
        var notes = PartnerToolsLine;
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
        Assert.Contains("Partner tools for this use case", system, StringComparison.Ordinal);
        Assert.Contains("BotPenguin", system, StringComparison.Ordinal);
        Assert.DoesNotContain("allowlist", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlogSection_keeps_partner_block_past_index_4()
    {
        var brief = ResearchBriefBuilder.Build(Ctx(FluffThenPartner()), ResearchBriefPhase.BlogSection);

        Assert.Contains("Partner tools for this use case", brief, StringComparison.Ordinal);
        Assert.Contains("PARTNER PAGE EXCERPTS", brief, StringComparison.Ordinal);
        Assert.Contains("BotPenguin", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("allowlist", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectSiteCopyParagraphs_prefers_partner_block_over_raw_Take5()
    {
        var selected = ResearchBriefBuilder.SelectSiteCopyParagraphs(FluffThenPartner());
        Assert.Contains(selected, p => p.Contains("Partner tools for this use case", StringComparison.Ordinal));
        Assert.Contains(selected, p => p.Contains("PARTNER PAGE EXCERPTS", StringComparison.Ordinal));
    }
}
