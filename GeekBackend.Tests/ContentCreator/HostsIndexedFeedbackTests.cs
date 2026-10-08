using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.Controllers.Rag;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The feedback shown beside each URL as it is entered (POST api/rag/hosts-indexed) is the same answer
/// the Profile save gives. It was a second implementation that read only the crawl's own counters, so
/// the form showed green for a competitor the index held nothing for (Jeff, 2026-10-05: "does not
/// surface when it should because all URL's feedback is valid").
/// </summary>
public sealed class HostsIndexedFeedbackTests
{
    private const string Rival = "https://rival.test";
    private const string Partner = "https://partner.test";

    private sealed class SignedIn : ICurrentUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    private static (RagController Controller, List<string?> SearchedAs) Build(
        Func<Guid, GeekCrawlerRagQueryResult?> search, params string[] urls)
    {
        var rows = urls
            .Select(u => new GeekCrawlerRagHostIndex(u, new Uri(u).Host, true, Guid.NewGuid().ToString()))
            .ToList();
        var searchedAs = new List<string?>();
        var validator = new GccDeclaredUrlValidator(
            new GccCompetitorAnalysisResolverTests.FakeRag(rows, (runId, crawlType) =>
            {
                lock (searchedAs) searchedAs.Add(crawlType);
                return search(runId);
            }),
            new HttpGeekCrawlerRepository(
                new HttpClient(new GccProjectsControllerIndexGateTests.UsableRunHandler()) { BaseAddress = new Uri("https://crawler.test") },
                NullLogger<HttpGeekCrawlerRepository>.Instance),
            NullLogger<GccDeclaredUrlValidator>.Instance);
        // hosts-indexed does not touch the library writer.
        return (new RagController(new SignedIn(), null!, validator), searchedAs);
    }

    private static JsonElement Results(IActionResult result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value))
            .RootElement.GetProperty("results");

    [Fact]
    public async Task A_url_whose_crawl_counted_pages_the_index_does_not_hold_is_not_usable()
    {
        var (controller, _) = Build(GccProjectsControllerIndexGateTests.HoldsNothing, Rival);

        var result = await controller.HostsIndexed(
            new RagController.HostsIndexedRequest([Rival], CrawlTypes.Competitors), CancellationToken.None);

        var row = Assert.Single(Results(result).EnumerateArray());
        Assert.True(row.GetProperty("indexed").GetBoolean());
        Assert.False(row.GetProperty("usable").GetBoolean());
        Assert.Contains("a search of the index finds nothing from it", row.GetProperty("reason").GetString());
        // The counters alone would have called it usable: 40 pages, 400 chunks.
        Assert.Equal(40, row.GetProperty("pages").GetInt32());
    }

    [Fact]
    public async Task A_url_the_index_holds_pages_for_is_usable_and_is_searched_as_its_list()
    {
        var (controller, searchedAs) = Build(GccProjectsControllerIndexGateTests.Holds, Partner);

        var result = await controller.HostsIndexed(
            new RagController.HostsIndexedRequest([Partner], "Partner"), CancellationToken.None);

        var row = Assert.Single(Results(result).EnumerateArray());
        Assert.True(row.GetProperty("usable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reason").ValueKind);
        Assert.Equal([CrawlTypes.Partner], searchedAs);
    }

    /// <summary>
    /// The list is part of the question. With none named there is no looser check to fall back to --
    /// that would be a second meaning of "valid" at the place validation happens first.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rivals")]
    public async Task Without_a_known_list_the_check_is_refused_and_nothing_is_searched(string? crawlType)
    {
        var (controller, searchedAs) = Build(GccProjectsControllerIndexGateTests.Holds, Rival);

        var result = await controller.HostsIndexed(
            new RagController.HostsIndexedRequest([Rival], crawlType), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(searchedAs);
    }

    [Fact]
    public async Task A_search_that_fails_is_not_an_answer_about_the_url()
    {
        var (controller, _) = Build(
            runId => new GeekCrawlerRagQueryResult { RunId = runId, Pages = [], Failed = true }, Rival);

        var result = await controller.HostsIndexed(
            new RagController.HostsIndexedRequest([Rival], CrawlTypes.Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    /// <summary>
    /// tipalti.com, 2026-10-08: one host, a partner run and a competitor run, both complete and both
    /// indexed. Asked by host alone, the index answered the competitor run; the partner probe on it
    /// matched nothing, and the partner was excluded with "its crawl finished, but a search of the
    /// index finds nothing from it". The list is part of the question to the index, not only to the
    /// probe that follows it.
    /// </summary>
    [Theory]
    [InlineData(CrawlTypes.Partner)]
    [InlineData(CrawlTypes.Competitors)]
    public async Task The_index_is_asked_for_the_list_the_urls_came_from(string crawlType)
    {
        var rows = new[]
        {
            new GeekCrawlerRagHostIndex(Rival, "rival.test", true, Guid.NewGuid().ToString(), crawlType),
        };
        var rag = new GccCompetitorAnalysisResolverTests.FakeRag(
            rows, (runId, _) => GccProjectsControllerIndexGateTests.Holds(runId));
        var validator = new GccDeclaredUrlValidator(
            rag,
            new HttpGeekCrawlerRepository(
                new HttpClient(new GccProjectsControllerIndexGateTests.UsableRunHandler()) { BaseAddress = new Uri("https://crawler.test") },
                NullLogger<HttpGeekCrawlerRepository>.Instance),
            NullLogger<GccDeclaredUrlValidator>.Instance);

        await validator.AnswerAsync([Rival], crawlType, CancellationToken.None);

        Assert.Equal([crawlType], rag.HostsAskedAs);
    }
}
