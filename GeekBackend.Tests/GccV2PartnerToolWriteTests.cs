using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public sealed class GccV2PartnerToolWriteTests
{
    [Fact]
    public void RenderSourceAttribution_emits_blockquote_cite_typographic_quotes_and_visit_link()
    {
        const string url = "https://botpenguin.com/product";
        const string quote = "BotPenguin automates conversational support for marketing teams.";
        var html = GccV2ToolSectionRenderer.RenderSourceAttribution(url, quote, "BotPenguin");

        Assert.Contains($"<blockquote cite=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains(
            $"<p>\u201C{quote}\u201D</p>",
            html,
            StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">Visit BotPenguin</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBlockQuoteText_wraps_verbatim_text_in_typographic_quotes()
    {
        var formatted = GccV2ToolSectionRenderer.FormatBlockQuoteText(
            "Pipedrive is a sales CRM built for small teams.");
        Assert.Equal("\u201CPipedrive is a sales CRM built for small teams.\u201D", formatted);
    }

    [Fact]
    public void PickVerbatimQuote_selects_first_usable_paragraph()
    {
        var page = new GccQuoteablePage(
            "https://pipedrive.com/crm",
            "Pipedrive CRM",
            [],
            [
                "Short.",
                "Pipedrive is a sales-focused CRM that helps small teams manage pipelines and close deals faster.",
            ]);

        var quote = GccV2ToolResearchExtractor.PickVerbatimQuote(page);
        Assert.Equal(
            "Pipedrive is a sales-focused CRM that helps small teams manage pipelines and close deals faster.",
            quote);
    }

    [Fact]
    public void PickBestVerbatimQuote_falls_back_to_shorter_verbatim_paragraph()
    {
        var page = new GccQuoteablePage(
            "https://pipedrive.com/crm",
            "Pipedrive CRM",
            [],
            ["Pipedrive helps sales teams win today."]);

        Assert.Equal("", GccV2ToolResearchExtractor.PickVerbatimQuote(page));
        Assert.Equal(
            "Pipedrive helps sales teams win today.",
            GccV2ToolResearchExtractor.PickBestVerbatimQuote(page));
    }

    [Fact]
    public void RequireSourceAttributionHtml_emits_blockquote_and_visit_link()
    {
        const string url = "https://pipedrive.com/product";
        const string quote = "Pipedrive is a sales-focused CRM built for growing teams.";
        var html = GccV2PartnerToolWriteService.RequireSourceAttributionHtml(url, quote, "Pipedrive");

        Assert.Contains("<blockquote", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"cite=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains("Visit Pipedrive", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSourceAttributionHtml_throws_when_quote_missing()
    {
        const string url = "https://pipedrive.com/product";
        var ex = Assert.Throws<ContentGenerationException>(() =>
            GccV2PartnerToolWriteService.RequireSourceAttributionHtml(url, "", "Pipedrive"));
        Assert.Contains("verbatim source blockquote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAttributionExcerpt_returns_source_quote_only()
    {
        var research = new GccV2ExtractedToolResearch(
            "BotPenguin",
            "Paraphrased summary line.",
            "Does chat automation.",
            [],
            [],
            "",
            "",
            "BotPenguin helps teams automate chat on every channel.");
        Assert.Equal(
            "BotPenguin helps teams automate chat on every channel.",
            GccV2ToolResearchExtractor.BuildAttributionExcerpt(research));
    }

    [Fact]
    public void ResolveSourceQuote_prefers_crawled_quote_over_llm_paraphrase()
    {
        const string pageText =
            "Pipedrive is a sales-focused CRM that helps small teams manage pipelines and close deals faster.";
        var crawled = GccV2ToolResearchExtractor.PickVerbatimQuote(
            new GccQuoteablePage("https://pipedrive.com", "CRM", [], [pageText]));

        var resolved = GccV2ToolResearchExtractor.ResolveSourceQuote(
            crawled,
            "A CRM tool for managing sales pipelines.",
            pageText);

        Assert.Equal(crawled, resolved);
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
