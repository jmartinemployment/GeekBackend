using System.Net;
using System.Text;
using GeekAPI.Controllers.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A project may not declare a partner or competitor URL with no indexed crawl behind it.
///
/// <para>
/// <c>plans/validate-partner-competitor-urls.md</c> has mandated this since 2026-09-17 and the form
/// has coloured each URL green or red since <c>fd5c920</c> -- but nothing refused. That commit's
/// only submit guard read <c>siteUrls.length</c>, never an index answer, so the partner and
/// competitor verdicts were rendered and consumed by nothing.
/// </para>
///
/// <para>
/// These assert the server half, which is what makes it a boundary rather than an affordance
/// (<c>CLAUDE.md</c> §2: "a boundary is only fail-closed if code rejects the bad input"). Every
/// refusal case also asserts the repository was never called -- a gate that refuses after
/// persisting has not refused.
/// </para>
/// </summary>
public class GccProjectsControllerIndexGateTests
{
    private const string Partner = "https://partner.test";
    private const string Competitor = "https://rival.test";

    private static GeekCrawlerRagHostIndex Indexed(string url) =>
        new(url, new Uri(url).Host, true, Guid.NewGuid().ToString());

    private static GeekCrawlerRagHostIndex NotIndexed(string url) =>
        new(url, new Uri(url).Host, false, null);

    /// <summary>
    /// Stands in for the repository service. Records whether it was reached at all, which is the
    /// assertion that matters: the gate must run before anything is written.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        /// <summary>A minimally valid project row, so a reached repository answers rather than throws.</summary>
        private const string SavedProjectJson =
            """
            {"id":"11111111-1111-1111-1111-111111111111","clientId":"22222222-2222-2222-2222-222222222222",
             "name":"Q4 programme","code":null,"description":null,"status":"active","siteUrl":null,
             "projectSiteRunId":null,"department":null,"partnerUrls":[],"competitorUrls":[],
             "startDate":"2026-10-01","dueDate":null,"finishedDate":null,"estimatedHours":null,
             "budget":null,"budgetCurrency":null,"createdAtUtc":"2026-10-01T00:00:00Z",
             "updatedAtUtc":"2026-10-01T00:00:00Z"}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SavedProjectJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (GccProjectsController Controller, RecordingHandler Repo) Build(
        params GeekCrawlerRagHostIndex[] rows)
    {
        var handler = new RecordingHandler();
        var repo = new HttpGccRepository(
            new HttpClient(handler) { BaseAddress = new Uri("https://repo.test") },
            NullLogger<HttpGccRepository>.Instance);

        var controller = new GccProjectsController(
            repo,
            // The index fake already written for GccCompetitorAnalysisResolverTests -- one
            // implementation of IGeekCrawlerRagClient for the suite, not a second that can drift.
            new GccCompetitorAnalysisResolverTests.FakeRag(rows),
            NullLogger<GccProjectsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim("sub", "operator-1")], "test")),
                },
            },
        };

        return (controller, handler);
    }

    private static GccProjectsController.CreateProjectRequest CreateRequest(
        IReadOnlyList<string>? partners = null, IReadOnlyList<string>? competitors = null) =>
        new(
            ClientId: Guid.NewGuid(),
            IdempotencyKey: Guid.NewGuid(),
            Name: "Q4 programme",
            StartDate: new DateOnly(2026, 10, 1),
            PartnerUrls: partners,
            CompetitorUrls: competitors);

    private static GccProjectsController.UpdateProjectRequest UpdateRequest(
        IReadOnlyList<string>? partners = null, IReadOnlyList<string>? competitors = null) =>
        new(
            Name: "Q4 programme",
            StartDate: new DateOnly(2026, 10, 1),
            PartnerUrls: partners,
            CompetitorUrls: competitors);

    private static int StatusOf(ActionResult<GccProjectDto> result) => result.Result switch
    {
        ObjectResult o => o.StatusCode ?? 0,
        StatusCodeResult s => s.StatusCode,
        _ => 200,
    };

    private static string BodyOf(ActionResult<GccProjectDto> result) =>
        result.Result is ObjectResult { Value: string text } ? text : string.Empty;

    [Fact]
    public async Task AnUnindexedCompetitorUrlIsRefusedAndNothingIsWritten()
    {
        var (controller, repo) = Build(Indexed(Partner), NotIndexed(Competitor));

        var result = await controller.Create(CreateRequest([Partner], [Competitor]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Competitor, BodyOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AnUnindexedPartnerUrlIsRefusedTheSameWay()
    {
        // Partner and competitor are one rule. A second implementation for competitors is how the
        // two came to differ everywhere else in this pipeline.
        var (controller, repo) = Build(NotIndexed(Partner), Indexed(Competitor));

        var result = await controller.Create(CreateRequest([Partner], [Competitor]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task EveryUrlIndexedReachesTheRepository()
    {
        var (controller, repo) = Build(Indexed(Partner), Indexed(Competitor));

        await controller.Create(CreateRequest([Partner], [Competitor]), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task EmptyListsBlockNothingAndAskNothing()
    {
        // "an empty list blocks nothing" -- and a call worth not making is not made.
        var (controller, repo) = Build();

        await controller.Create(CreateRequest([], []), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task AUrlWithNoAnswerIsNotIndexed()
    {
        // One question, one answer. A URL the index returned nothing for has no index behind it --
        // never crawled, will not parse, or the index could not be asked. Same answer, same fix.
        var (controller, repo) = Build();

        var result = await controller.Create(CreateRequest([Partner], []), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task UpdateIsGatedToo()
    {
        // The route a form gate cannot cover: updateProject has no call site in the UI, so PUT is
        // reachable only by direct API call.
        var (controller, repo) = Build(NotIndexed(Competitor));

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest([], [Competitor]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Competitor, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task UpdateWithEveryUrlIndexedReachesTheRepository()
    {
        var (controller, repo) = Build(Indexed(Partner), Indexed(Competitor));

        await controller.Update(
            Guid.NewGuid(), UpdateRequest([Partner], [Competitor]), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task UpdateRefusesAUrlWithNoAnswerToo()
    {
        var (controller, repo) = Build();

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest([Partner], []), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }
}
