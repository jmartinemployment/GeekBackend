using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Blog and pillar link their tool mentions to /tools, and this is the wire that makes it possible:
/// the tools the publisher's own site links under the topic, read off the crawl at generate time.
///
/// <para>
/// The prompt rule for it already existed and was already tested -- by a test that set
/// KnownCrawlTools on the context by hand. That passes while production leaves it empty forever,
/// which is exactly what happened: the rule rendered in a test and never once in a real generate.
/// So these assert the resolver that fills it, against crawled blocks rather than a context the test
/// built itself.
/// </para>
/// </summary>
public class GccKnownToolsResolverTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeRunPages(IReadOnlyList<GeekCrawlerPageDto> pages) : IGccCrawlPageReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>(
                [.. pages.Skip(offset).Take(limit)]);
        }
    }

    private sealed class ThrowingPages : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            throw new HttpRequestException("repository down");
    }

    private static GccCreateDto Create(string topic, Guid? runId) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "blog",
        Topic: topic,
        Notes: null,
        ProjectSiteRunId: runId,
        SiteSectionJson: null,
        BriefJson: null,
        ResearchJson: null,
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);

    private static GeekCrawlerPageDto Page(string url, string blocksJson) => new(
        Id: Guid.NewGuid(),
        RunId: RunId,
        Origin: "https://geek.test",
        Url: url,
        FinalUrl: url,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: null,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow,
        Title: "Use cases",
        Excerpt: null,
        ContentHtml: null,
        Blocks: JsonDocument.Parse(blocksJson).RootElement);

    /// <summary>
    /// A heading with a paragraph under it whose anchors are the tools — the shape the crawler emits
    /// and the shape the site's own use-case lists actually have.
    /// </summary>
    private static GeekCrawlerPageDto UseCasePage() => Page(
        "https://geek.test/use-cases/accounting",
        """
        [
          {"kind":"heading","level":2,"text":"Automated Accounts Payable"},
          {"kind":"paragraph","text":"Top tools for automated accounts payable.","anchors":[
            {"label":"Melio","href":"/tools/accounting/melio"},
            {"label":"Dext","href":"/tools/accounting/dext"},
            {"label":"Lightyear","href":"/tools/accounting/lightyear"}
          ]}
        ]
        """);

    private static GccKnownToolsResolver Resolver(IGccCrawlPageReader pages) =>
        new(pages, NullLogger<GccKnownToolsResolver>.Instance);

    [Fact]
    public async Task The_tools_the_site_links_under_the_topic_reach_generation()
    {
        var tools = await Resolver(new FakeRunPages([UseCasePage()]))
            .ResolveAsync(Create("Automated Accounts Payable", RunId));

        Assert.Equal(["Melio", "Dext", "Lightyear"], tools.Select(t => t.Name));
    }

    [Fact]
    public async Task Each_tool_carries_the_href_the_crawl_found_it_at()
    {
        // Not the link the draft sets -- AppendKnownToolsBrief derives /tools/{dept}/{slug} from the
        // name and names this one as the source the reader must not be sent to.
        var tools = await Resolver(new FakeRunPages([UseCasePage()]))
            .ResolveAsync(Create("Automated Accounts Payable", RunId));

        Assert.Equal("/tools/accounting/melio", tools.Single(t => t.Name == "Melio").Href);
    }

    [Fact]
    public async Task A_create_with_no_project_site_run_resolves_empty_rather_than_refusing()
    {
        var tools = await Resolver(new FakeRunPages([UseCasePage()]))
            .ResolveAsync(Create("Automated Accounts Payable", null));

        Assert.Empty(tools);
    }

    [Fact]
    public async Task A_topic_the_site_does_not_cover_resolves_empty()
    {
        var tools = await Resolver(new FakeRunPages([UseCasePage()]))
            .ResolveAsync(Create("Quantum Cryptography Consulting", RunId));

        Assert.Empty(tools);
    }

    [Fact]
    public async Task A_repository_failure_resolves_empty_rather_than_failing_the_generate()
    {
        // A crawl that cannot be read is "we do not know which tools". Refusing here would block
        // every create on a site whose crawl has not landed.
        var tools = await Resolver(new ThrowingPages())
            .ResolveAsync(Create("Automated Accounts Payable", RunId));

        Assert.Empty(tools);
    }

    [Fact]
    public async Task Paging_stops_at_the_end_of_the_run_rather_than_asking_forever()
    {
        // One short page means the run is exhausted: a second call would be one more repository
        // round trip per generate for nothing.
        var pages = new FakeRunPages([UseCasePage()]);

        await Resolver(pages).ResolveAsync(Create("Automated Accounts Payable", RunId));

        Assert.Equal(1, pages.Calls);
    }

    [Fact]
    public async Task A_tool_linked_twice_on_the_page_is_named_once()
    {
        var page = Page(
            "https://geek.test/use-cases/accounting",
            """
            [
              {"kind":"heading","level":2,"text":"Automated Accounts Payable"},
              {"kind":"paragraph","text":"Mobile copy.","anchors":[
                {"label":"Melio","href":"/tools/accounting/melio"}
              ]},
              {"kind":"paragraph","text":"Desktop copy.","anchors":[
                {"label":"Melio","href":"/tools/accounting/melio"}
              ]}
            ]
            """);

        var tools = await Resolver(new FakeRunPages([page]))
            .ResolveAsync(Create("Automated Accounts Payable", RunId));

        Assert.Equal(["Melio"], tools.Select(t => t.Name));
    }
}
