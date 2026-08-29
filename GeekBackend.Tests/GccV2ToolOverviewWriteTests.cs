using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests;

public sealed class GccV2ToolOverviewWriteTests
{
    [Fact]
    public void InjectOnSiteToolLinks_adds_h3_hrefs_without_external_urls()
    {
        var sections = new List<Section>
        {
            new("h2", "Overview", [new TextParagraph([new Run("Framing.")])], null, []),
            new("h2", "Tools for AI Chatbots", [], null, []),
        };

        var partnerLinks = new List<(string Name, string OnSiteHref)>
        {
            ("BotPenguin", "/tools/marketing/bot-penguin"),
            ("ManyChat", "/tools/marketing/manychat"),
        };

        var updated = GccV2ToolOverviewWriteService.InjectOnSiteToolLinks(
            sections,
            partnerLinks,
            "Tools for AI Chatbots");

        var toolsSection = updated.Single(s => s.Heading.Contains("Tools for", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, toolsSection.Children.Count);
        Assert.All(toolsSection.Children, child =>
        {
            Assert.StartsWith("/tools/", child.Href ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain("http", child.Href ?? "", StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ResolveToolsHeading_prefers_outline_tools_row()
    {
        var outline = new List<GccV2OutlineSection>
        {
            new("overview", "Overview", "problem", []),
            new("tools-index", "Tools for CRM Software", "advance", ["Pipedrive"]),
        };

        Assert.Equal(
            "Tools for CRM Software",
            GccV2ToolOverviewWriteService.ResolveToolsHeading(outline, "CRM Software"));
    }

    [Fact]
    public void ExportPathFor_overview_vs_partner()
    {
        Assert.Equal(
            "tools/ai-chatbots.html",
            GccV2HtmlExportServiceExtensions.ExportPathFor("tool", "ai-chatbots", "overview"));
        Assert.Equal(
            "tools/marketing/bot-penguin.html",
            GccV2HtmlExportServiceExtensions.ExportPathFor("tool", "bot-penguin", "partner"));
    }
}

internal static class GccV2HtmlExportServiceExtensions
{
    public static string ExportPathFor(string contentType, string slug, string? toolPageKind) =>
        contentType == "tool" && string.Equals(toolPageKind, "overview", StringComparison.OrdinalIgnoreCase)
            ? $"tools/{slug}.html"
            : contentType == "tool"
                ? $"tools/marketing/{slug}.html"
                : $"{slug}.html";
}
