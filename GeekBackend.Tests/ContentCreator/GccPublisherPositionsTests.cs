using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The publisher's own positions -- the named sections of their site page -- reach the writer as the
/// publisher's own, by heading, with their text (Jeff, 2026-10-06: "writer needs to use my The
/// Methodology instead of inventing its own"; "All those headings are food or should be used in prose
/// as it grounds").
/// </summary>
public sealed class GccPublisherPositionsTests
{
    private const string Site = "https://geekatyourspot.com";

    private static JsonElement Blocks(params object[] blocks) => JsonSerializer.SerializeToElement(blocks);

    private static object H(int level, string text) => new { kind = "heading", level, text };
    private static object P(string text) => new { kind = "paragraph", text };
    private static object Li(string text) => new { kind = "listItem", text };
    private static object Row(params string[] cells) => new { kind = "row", cells };

    [Fact]
    public void The_page_is_cut_at_its_second_level_headings_with_the_text_under_each()
    {
        var blocks = Blocks(
            H(1, "Geek At Your Spot"),
            P("Intro under the title; not a position."),
            H(2, "The Methodology"),
            P("First we diagnose. Then we configure. Then we hand over."),
            H(3, "Diagnose"),
            Li("Map the approval chain."),
            Row("Stage", "Owner"),
            H(2, "Seamless Integrations"),
            P("Every tool we implement connects to the ledger you already run."),
            H(2, "Empty heading"),
            H(2, "Clone Yourself, Work 24/7"),
            P("Automation that runs while you do not."),
            H(2, "The Methodology"),
            P("A duplicate heading further down is not a second position."));

        var positions = GccPublisherPositionsReader.Cut(blocks, Site);

        Assert.Equal(["The Methodology", "Seamless Integrations", "Clone Yourself, Work 24/7"], positions.Select(p => p.Heading));
        Assert.Equal(
            ["First we diagnose. Then we configure. Then we hand over.", "Diagnose", "Map the approval chain.", "Stage | Owner"],
            positions[0].Paragraphs);
        Assert.All(positions, p => Assert.Equal(Site, p.Url));
        Assert.Empty(GccPublisherPositionsReader.Cut(null, Site));
        Assert.Empty(GccPublisherPositionsReader.Cut(Blocks(H(1, "Title only"), P("No sections.")), Site));
    }

    [Fact]
    public async Task The_site_page_is_read_from_the_crawl_run_and_a_missing_or_empty_page_is_a_warning()
    {
        var run = Guid.NewGuid();
        var withPage = new Pages(new Dictionary<string, JsonElement>
        {
            ["https://www.geekatyourspot.com/"] = Blocks(H(2, "The Methodology"), P("Diagnose, configure, hand over.")),
        });
        var reader = new GccPublisherPositionsReader(withPage, NullLogger<GccPublisherPositionsReader>.Instance);

        var (positions, warning) = await reader.ReadAsync(run, Site);
        var (none, missing) = await new GccPublisherPositionsReader(new Pages(new Dictionary<string, JsonElement>()), NullLogger<GccPublisherPositionsReader>.Instance)
            .ReadAsync(run, Site);
        var (empty, blank) = await new GccPublisherPositionsReader(
                new Pages(new Dictionary<string, JsonElement> { [Site] = Blocks(H(1, "Title")) }),
                NullLogger<GccPublisherPositionsReader>.Instance)
            .ReadAsync(run, Site);

        Assert.Null(warning);
        Assert.Equal("The Methodology", Assert.Single(positions).Heading);
        Assert.Empty(none);
        Assert.Contains("is not in its crawl run", missing!, StringComparison.Ordinal);
        Assert.Empty(empty);
        Assert.Contains("has no sections with text", blank!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_block_hands_the_writer_every_position_by_heading_and_says_how_each_kind_is_used()
    {
        var positions = new List<GccPublisherPosition>
        {
            new("The Methodology", ["Diagnose, configure, hand over."], Site),
            new("Artificial Intelligence Use Cases", ["Invoice capture.", "Approval routing."], Site),
        };

        var block = GccPublisherPositions.Block(positions, "Automated Approval Workflows");

        Assert.StartsWith(GccPublisherPositions.Header, block, StringComparison.Ordinal);
        Assert.Contains($"(from {Site})", block, StringComparison.Ordinal);
        Assert.Contains("applied to Automated Approval Workflows", block, StringComparison.Ordinal);
        Assert.Contains("[The Methodology]", block, StringComparison.Ordinal);
        Assert.Contains("- Diagnose, configure, hand over.", block, StringComparison.Ordinal);
        Assert.Contains("[Artificial Intelligence Use Cases]", block, StringComparison.Ordinal);
        Assert.Contains("walks its stages, in order", block, StringComparison.Ordinal);
        Assert.Contains("answer the publisher's questions when booking", block, StringComparison.Ordinal);
        Assert.Contains("reprint a passage", block, StringComparison.Ordinal);
        Assert.Equal(string.Empty, GccPublisherPositions.Block([], "x"));
        Assert.Equal(string.Empty, GccPublisherPositions.Block(null, "x"));
    }

    [Fact]
    public void The_positions_ride_the_research_document_and_the_evidence_block_carries_them()
    {
        var research = new GccResearchDocument(
            null, [], PublisherPositions: [new GccPublisherPosition("The Methodology", ["Diagnose first."], Site)]);
        var json = GccResearchFetchService.Serialize(research);
        var create = new GccCreateDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pillar", "Accounts Payable: Automated Approval Workflows",
            null, null, null, null, json, "draft", DateTime.UtcNow, DateTime.UtcNow, "accounting", Guid.NewGuid());

        var roundTrip = GccResearchFetchService.Deserialize(json)!.PublisherPositions;
        var block = GccGenerateService.BuildPublisherPositionsBlock(create);

        Assert.Equal("The Methodology", Assert.Single(roundTrip!).Heading);
        Assert.Contains("[The Methodology]", block, StringComparison.Ordinal);
        Assert.Contains("applied to Automated Approval Workflows", block, StringComparison.Ordinal);
    }

    private sealed class Pages(IReadOnlyDictionary<string, JsonElement> byUrl) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>(
                byUrl.Select(kv => new GeekCrawlerPageDto(
                    Guid.NewGuid(), runId, kv.Key, kv.Key, kv.Key, 200, true, null, null, DateTimeOffset.UtcNow,
                    Title: "Home", Blocks: kv.Value)).ToList());

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }
}
