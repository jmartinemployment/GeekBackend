using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests;

public sealed class GccV2PartnerToolWriteTests
{
    [Fact]
    public void RenderSourceAttribution_emits_blockquote_cite_and_visit_link()
    {
        const string url = "https://botpenguin.com/product";
        var html = GccV2ToolSectionRenderer.RenderSourceAttribution(
            url,
            "BotPenguin automates conversational support for marketing teams.",
            "BotPenguin");

        Assert.Contains($"<blockquote cite=\"{url}\">", html, StringComparison.Ordinal);
        Assert.Contains("<p>BotPenguin automates conversational support for marketing teams.</p>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{url}\">Visit BotPenguin</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAttributionExcerpt_prefers_summary_then_whatItDoes()
    {
        var research = new GccV2ExtractedToolResearch(
            "BotPenguin",
            "Summary line.",
            "Does chat automation.",
            [],
            [],
            "",
            "");
        Assert.Equal("Summary line.", GccV2ToolResearchExtractor.BuildAttributionExcerpt(research));
    }

    [Fact]
    public void BuildSourceAttributionHtml_uses_fallback_when_excerpt_empty()
    {
        const string url = "https://pipedrive.com/product";
        var html = GccV2PartnerToolWriteService.BuildSourceAttributionHtml(url, "", "Pipedrive");
        Assert.NotNull(html);
        Assert.Contains($"<blockquote cite=\"{url}\">", html, StringComparison.Ordinal);
        Assert.Contains("Pipedrive", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPartnerToolSectionPrompt_includes_implementation_guidance()
    {
        var builder = new GccV2ToolPagePromptBuilder();
        var context = new ProjectGenerationContext(
            ProjectName: "CRM",
            ProjectUrl: "https://example.com",
            TargetKeyword: "CRM automation",
            Department: "marketing",
            SiteName: "Example Co",
            DetectedTone: "Professional",
            DetectedFocus: "CRM",
            CrawledHeadings: [],
            CrawledParagraphs: [],
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: [],
            PublisherName: "Example Co",
            PublisherLogoUrl: "https://example.com/logo.png",
            AuthorName: "Author",
            ArticleBaseUrl: "https://example.com",
            BlogBaseUrl: "https://example.com/blog",
            ToolBaseUrl: "https://example.com/tools",
            ImplementerPositioning: "AI implementer",
            Provider: LlmProviderType.OpenAi,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null);
        var metadata = new ArticleMetadataDraft("CRM", "Meta", ["CRM"], []);
        var app = new SoftwareApplicationDescriptor("Pipedrive", "CRM tool", null);

        var request = builder.BuildPartnerToolSectionPrompt(
            context,
            metadata,
            app,
            "pipedrive",
            "Implementation Considerations",
            2,
            4,
            null,
            null);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Accelerated deployment", system, StringComparison.Ordinal);
        Assert.Contains("Data model design", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolPageSchemaBuilder_subjectOf_uses_pillar_url_not_tool_url()
    {
        const string pillarUrl = "https://example.com/marketing/ai-chatbots";
        const string toolUrl = "https://example.com/tools/marketing/bot-penguin";
        var metadata = new ContentMetadata(
            "BotPenguin",
            "Tool meta",
            "Author",
            "Publisher",
            "https://example.com/logo.png",
            toolUrl,
            "https://example.com/logo.png",
            DateTime.UtcNow,
            DateTime.UtcNow,
            ["AI Chatbots"],
            1200);

        var json = GccV2ToolPageSchemaBuilder.BuildToolPage(
            metadata,
            pillarUrl,
            new SoftwareApplicationDescriptor(
                "BotPenguin",
                "Tool meta",
                toolUrl));

        Assert.Contains(pillarUrl, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"@id\": \"" + toolUrl + "\"", json, StringComparison.Ordinal);
    }
}
