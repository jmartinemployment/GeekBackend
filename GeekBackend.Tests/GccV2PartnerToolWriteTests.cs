using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Workflow.Domain.Entities;

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
    public void ToolPageSchemaBuilder_subjectOf_uses_pillar_url_not_tool_url()
    {
        const string pillarUrl = "https://example.com/marketing/ai-chatbots";
        const string toolUrl = "https://example.com/tools/marketing/bot-penguin";
        var metadata = new GeekAPI.Services.Workflow.DTOs.ContentMetadata(
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
            new GeekAPI.Services.Workflow.Services.SchemaBuilders.SoftwareApplicationDescriptor(
                "BotPenguin",
                "Tool meta",
                toolUrl));

        Assert.Contains(pillarUrl, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"@id\": \"" + toolUrl + "\"", json, StringComparison.Ordinal);
    }
}
