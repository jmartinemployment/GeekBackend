using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Where the tools and the writing assignment come from: the project-site crawl, not Site Analyzer.
/// </summary>
/// <remarks>
/// <para>
/// This read Site Analyzer until 2026-10-01 and handed it <c>project.ProjectSiteRunId</c> — a
/// Geek-Crawler-v2 run id — as a profile id, the local even named <c>profileId</c>. The service was
/// gone, so the call could only fail, and this call site <b>threw</b> on failure. A project with a
/// site run and a keyword failed generation outright; a project with neither returned empty and
/// proceeded. Configuring the project correctly was what broke it, which is why no test caught it:
/// nothing constructed this class.
/// </para>
/// <para>
/// The same defect was found and fixed in <c>GccController</c> on 2026-09-23. It was recorded there as
/// the Create path's, and this one was read as "the old Workflow path" — but
/// <c>ContentGenerationOrchestrator</c> injects this class, and that is the live v1 output path.
/// </para>
/// </remarks>
public class ToolPageGeneratorCrawlHierarchyTests
{
    private static readonly Guid Run = Guid.NewGuid();

    private static ToolPageGenerator Build(params GeekCrawlerPageDto[] pages) =>
        // The schema and prompt builders are untouched by this read -- it resolves structure and
        // returns, with no model call and no markup -- so the test does not stand up two fakes that
        // would only assert they were never used.
        new(null!, null!,
            new GccProjectSiteStructureReader(new FakePages(pages)),
            NullLogger<ToolPageGenerator>.Instance);

    private static Project Project(Guid? runId, string keyword) => new()
    {
        Name = "Acme",
        ProjectUrl = "https://acme.test",
        TargetKeyword = keyword,
        ProjectSiteRunId = runId,
    };

    /// <summary>One crawled page whose blocks carry the heading tree and its anchors.</summary>
    private static GeekCrawlerPageDto Page(string url, object[] blocks) => new(
        Id: Guid.NewGuid(),
        RunId: Run,
        Origin: url,
        Url: url,
        FinalUrl: url,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: null,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow,
        Title: "Acme",
        Excerpt: null,
        ContentHtml: null,
        Blocks: JsonSerializer.SerializeToElement(blocks));

    private static object Heading(int level, string text) =>
        new Dictionary<string, object> { ["kind"] = "heading", ["level"] = level, ["text"] = text };

    private static object Paragraph(string text, params (string Label, string Href)[] anchors) =>
        new Dictionary<string, object>
        {
            ["kind"] = "paragraph",
            ["text"] = text,
            ["anchors"] = anchors
                .Select(a => new Dictionary<string, string> { ["label"] = a.Label, ["href"] = a.Href })
                .ToArray(),
        };

    [Fact]
    public async Task The_matched_section_and_the_tools_linked_under_it_come_from_the_crawl()
    {
        var page = Page("https://acme.test/marketing", [
            Heading(2, "Smart Chatbots for Marketing"),
            Paragraph(
                "These are the chatbot tools we implement for marketing teams.",
                ("BotPenguin", "/tools/marketing/bot-penguin"),
                ("ManyChat", "/tools/marketing/many-chat"),
                ("Pipedrive", "/tools/marketing/pipedrive")),
            Heading(3, "Lead capture"),
            Paragraph("Chatbots qualify leads before a human sees them."),
        ]);

        var hierarchy = await Build(page).ListCrawlHierarchyAsync(
            Project(Run, "Smart Chatbots for Marketing"), CancellationToken.None);

        Assert.Equal(3, hierarchy.Tools.Count);
        Assert.Contains(hierarchy.Tools, t => t.Name == "BotPenguin" && t.Href == "/tools/marketing/bot-penguin");

        Assert.NotNull(hierarchy.Assignment);
        Assert.Equal("Smart Chatbots for Marketing", hierarchy.Assignment!.Heading);
        // The heading's own depth, which the prompt renders as "(h2)". The crawl's blocks carry it and
        // the matcher now reports it; writing 0 here would tell the model the level was unknown.
        Assert.Equal(2, hierarchy.Assignment.Level);
        Assert.Equal(3, hierarchy.Assignment.Links.Count);
        Assert.Contains("Lead capture", hierarchy.Assignment.Children.Select(c => c.Heading));

        // The section's own prose, which ResearchBriefBuilder renders into the brief beneath the
        // heading. It was [] on the first cut of this rewire, defended by a comment asserting nothing
        // consumes it -- that builder does, and the path this replaced supplied it, so the brief had
        // quietly stopped carrying what the site already says here.
        Assert.Contains(
            hierarchy.Assignment.Paragraphs,
            paragraph => paragraph.Contains("chatbot tools we implement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_project_with_no_site_run_writes_without_site_structure()
    {
        var hierarchy = await Build().ListCrawlHierarchyAsync(
            Project(null, "Smart Chatbots for Marketing"), CancellationToken.None);

        Assert.Empty(hierarchy.Tools);
        Assert.Null(hierarchy.Assignment);
    }

    [Fact]
    public async Task A_project_with_no_keyword_writes_without_site_structure()
    {
        var hierarchy = await Build().ListCrawlHierarchyAsync(
            Project(Run, "   "), CancellationToken.None);

        Assert.Empty(hierarchy.Tools);
        Assert.Null(hierarchy.Assignment);
    }

    [Fact]
    public async Task A_run_holding_no_pages_is_empty_rather_than_an_exception()
    {
        // This is the case that used to throw. The old call reached a service that no longer exists
        // and turned its 503 into a ContentGenerationException, so a configured project could not
        // generate at all. No pages is a reason to write without site structure, not to fail.
        var hierarchy = await Build().ListCrawlHierarchyAsync(
            Project(Run, "Smart Chatbots for Marketing"), CancellationToken.None);

        Assert.Empty(hierarchy.Tools);
        Assert.Null(hierarchy.Assignment);
    }

    [Fact]
    public async Task Nothing_on_the_site_matching_the_keyword_is_empty_not_an_exception()
    {
        var page = Page("https://acme.test/accounting", [
            Heading(2, "Accounts Payable Automation"),
            Paragraph("Invoice capture and approval routing."),
        ]);

        var hierarchy = await Build(page).ListCrawlHierarchyAsync(
            Project(Run, "Smart Chatbots for Marketing"), CancellationToken.None);

        Assert.Empty(hierarchy.Tools);
        Assert.Null(hierarchy.Assignment);
    }

    [Fact]
    public async Task A_named_anchor_with_no_href_is_not_a_tool()
    {
        // A tool name without its href cites nothing, which is the rule the old extraction applied and
        // the reason a tool row carries both.
        var page = Page("https://acme.test/marketing", [
            Heading(2, "Smart Chatbots for Marketing"),
            Paragraph(
                "The tools we implement.",
                ("BotPenguin", "/tools/marketing/bot-penguin"),
                ("ManyChat", "/tools/marketing/many-chat")),
        ]);

        var hierarchy = await Build(page).ListCrawlHierarchyAsync(
            Project(Run, "Smart Chatbots for Marketing"), CancellationToken.None);

        Assert.All(hierarchy.Tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Href)));
    }

    private sealed class FakePages(IReadOnlyList<GeekCrawlerPageDto> pages) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult(offset == 0 ? pages : []);
    }
}
