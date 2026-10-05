using System.Net;
using System.Text;
using GeekAPI.Controllers.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
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
    private const string Site = "https://acme.test";

    /// <summary>Five of each, because five of each is the rule. Named so a test can single one out.</summary>
    private static string[] Partners => [.. Enumerable.Range(1, 5).Select(i => $"https://partner{i}.test")];
    private static string[] Competitors => [.. Enumerable.Range(1, 5).Select(i => $"https://rival{i}.test")];

    private static string Partner => Partners[0];
    private static string Competitor => Competitors[0];

    /// <summary>Every declared URL indexed — the state a saveable project is in.</summary>
    private static GeekCrawlerRagHostIndex[] AllIndexed =>
        [.. new[] { Site }.Concat(Partners).Concat(Competitors).Select(Indexed)];

    /// <summary>The same, with one URL swapped for a not-indexed answer.</summary>
    private static GeekCrawlerRagHostIndex[] AllIndexedExcept(string url) =>
        [.. AllIndexed.Where(r => !r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)), NotIndexed(url)];

    private static readonly Guid SiteRun = Guid.NewGuid();

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

        /// <summary>
        /// What was actually sent to be saved. "The gate let it through" and "the gate saved what
        /// the operator typed" are different claims, and only the body can tell them apart.
        /// </summary>
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SavedProjectJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Answers the crawler repository with a run good enough to write from, so "indexed" and
    /// "usable" only diverge where a test makes them.
    /// </summary>
    /// <summary>
    /// A crawl run whose counters all pass, answered for whichever run id is asked for -- so each
    /// declared URL keeps its own run and a search can be answered per run.
    /// </summary>
    internal sealed class UsableRunHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    UsableRunJson.Replace("RUN_ID", request.RequestUri!.Segments[^1].TrimEnd('/')),
                    Encoding.UTF8, "application/json"),
            });

        private const string UsableRunJson =
            """
            {"id":"RUN_ID","ownerUserId":"operator-1",
             "crawlType":"partner","status":"complete","seedUrlsJson":"[]","seedKey":null,
             "hostProgressJson":null,"errorSummary":null,"createdAtUtc":"2026-09-01T00:00:00Z",
             "startedAtUtc":"2026-09-01T00:00:00Z","completedAtUtc":"2026-09-01T01:00:00Z",
             "contentReadyAt":"2026-09-01T01:00:00Z","crawlReportJson":null,"ragState":"indexed",
             "ragChunksUpserted":400,"ragPagesEnglish":40,
             "ragIndexedAtUtc":"2026-09-01T02:00:00Z"}
            """;
    }

    /// <summary>A search that returns a page: the run holds chunks.</summary>
    internal static GeekCrawlerRagQueryResult Holds(Guid runId) =>
        new() { RunId = runId, Pages = [new GccQuoteablePage("https://found.test/page", "Found", [], ["Text."])] };

    /// <summary>A search that returns nothing: "No chunks for runId".</summary>
    internal static GeekCrawlerRagQueryResult HoldsNothing(Guid runId) =>
        new() { RunId = runId, Pages = [], Warning = $"No chunks for runId={runId}; notify-and-skip research", Retrieval = "empty" };

    private static (GccProjectsController Controller, RecordingHandler Repo) Build(
        params GeekCrawlerRagHostIndex[] rows) =>
        Build(rows, (runId, _) => Holds(runId));

    private static (GccProjectsController Controller, RecordingHandler Repo) Build(
        GeekCrawlerRagHostIndex[] rows,
        Func<Guid, string?, GeekCrawlerRagQueryResult?> search)
    {
        var handler = new RecordingHandler();
        var repo = new HttpGccRepository(
            new HttpClient(handler) { BaseAddress = new Uri("https://repo.test") },
            NullLogger<HttpGccRepository>.Instance);

        var crawlerRepo = new HttpGeekCrawlerRepository(
            new HttpClient(new UsableRunHandler()) { BaseAddress = new Uri("https://crawler.test") },
            NullLogger<HttpGeekCrawlerRepository>.Instance);

        var controller = new GccProjectsController(
            repo,
            // The index fake already written for GccCompetitorAnalysisResolverTests -- one
            // implementation of IGeekCrawlerRagClient for the suite, not a second that can drift.
            new GccDeclaredUrlValidator(
                new GccCompetitorAnalysisResolverTests.FakeRag(rows, search),
                crawlerRepo,
                NullLogger<GccDeclaredUrlValidator>.Instance),
            // Profile save reaches neither the generate runner nor the must-mention builder.
            null!,
            null!,
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
            SiteUrl: Site,
            ProjectSiteRunId: SiteRun,
            PartnerUrls: partners,
            CompetitorUrls: competitors);

    private static GccProjectsController.UpdateProjectRequest UpdateRequest(
        IReadOnlyList<string>? partners = null, IReadOnlyList<string>? competitors = null) =>
        new(
            Name: "Q4 programme",
            StartDate: new DateOnly(2026, 10, 1),
            SiteUrl: Site,
            ProjectSiteRunId: SiteRun,
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
        var (controller, repo) = Build(AllIndexedExcept(Competitor));

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

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
        var (controller, repo) = Build(AllIndexedExcept(Partner));

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task EveryUrlIndexedReachesTheRepository()
    {
        var (controller, repo) = Build(AllIndexed);

        await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task FewerThanFivePartnersIsRefusedBeforeTheIndexIsAsked()
    {
        // The count costs nothing to check and the operator can act on it without waiting for a
        // round trip. "You need five" is a different problem from "this one has no crawl", and the
        // second is not worth reading until the first is solved.
        var (controller, repo) = Build(AllIndexed);

        var result = await controller.Create(
            CreateRequest([Partner], Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("Partner URLs: 1 declared, 5 required", BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task FewerThanFiveCompetitorsIsRefusedToo()
    {
        var (controller, repo) = Build(AllIndexed);

        var result = await controller.Create(
            CreateRequest(Partners, [Competitor]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("Competitor URLs: 1 declared, 5 required", BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task EmptyListsAreRefused_NotWavedThrough()
    {
        // "An empty list blocks nothing" was the old rule and it is gone: a project with no partners
        // has nothing to write a tool page from and nothing for a pillar to name.
        var (controller, repo) = Build(AllIndexed);

        var result = await controller.Create(CreateRequest([], []), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AnUnindexedSiteUrlIsRefusedLikeAnyOtherDeclaredUrl()
    {
        var (controller, repo) = Build(AllIndexedExcept(Site));

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Site, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task NoSiteUrlAtAllIsRefused()
    {
        // The form has always blocked this. The API accepted it, and the project then refused at
        // generate time with "has no project-site crawl run" -- a failure the operator could do
        // nothing about by then.
        var (controller, repo) = Build(AllIndexed);

        var result = await controller.Create(
            CreateRequest([], []) with { SiteUrl = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task NoProjectSiteRunIdIsRefused()
    {
        var (controller, repo) = Build(AllIndexed);

        var result = await controller.Create(
            CreateRequest([], []) with { ProjectSiteRunId = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AnUnreachableIndexRefusesRatherThanGuessing()
    {
        // No rows at all means the check did not run, which is not the same answer as "nothing is
        // indexed" -- HostsIndexedAsync returns [] when disabled, on a non-2xx and on a throw. It
        // still refuses: an answer never obtained is not evidence a crawl exists.
        var (controller, repo) = Build();

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("could not be reached", BodyOf(result), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repo.Calls);
    }

    /// <summary>
    /// 2026-10-04: a competitor's crawl passed every counter at save, and Generate found "No chunks"
    /// for its run and wrote without it. The counters say what the crawl recorded writing; only a
    /// search says what the index holds.
    /// </summary>
    [Fact]
    public async Task ACompetitorWhoseRunTheIndexHoldsNothingForIsRefusedAndNamed()
    {
        var rows = AllIndexed;
        var emptyRun = Guid.Parse(rows.Single(r => r.Url == Competitor).RunId!);
        var (controller, repo) = Build(rows, (runId, _) => runId == emptyRun ? HoldsNothing(runId) : Holds(runId));

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Competitor, BodyOf(result), StringComparison.Ordinal);
        Assert.Contains("a search of the index finds nothing from it", BodyOf(result), StringComparison.Ordinal);
        // Named by the URL the operator entered; a run id means nothing to them.
        Assert.DoesNotContain(emptyRun.ToString(), BodyOf(result), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task APartnerOrASiteTheIndexHoldsNothingForIsTreatedTheSame()
    {
        var rows = AllIndexed;
        var emptyPartner = Guid.Parse(rows.Single(r => r.Url == Partner).RunId!);
        var (partnerController, partnerRepo) = Build(
            rows, (runId, _) => runId == emptyPartner ? HoldsNothing(runId) : Holds(runId));
        var emptySite = Guid.Parse(rows.Single(r => r.Url == Site).RunId!);
        var (siteController, siteRepo) = Build(
            rows, (runId, _) => runId == emptySite ? HoldsNothing(runId) : Holds(runId));

        var partnerResult = await partnerController.Create(CreateRequest(Partners, Competitors), CancellationToken.None);
        var siteResult = await siteController.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Contains(Partner, BodyOf(partnerResult), StringComparison.Ordinal);
        Assert.Contains("The project site URL cannot be written from", BodyOf(siteResult), StringComparison.Ordinal);
        Assert.Equal(0, partnerRepo.Calls + siteRepo.Calls);
    }

    [Fact]
    public async Task EachUrlIsSearchedAsTheCrawlTypeOfItsList()
    {
        // Generate filters on the type of the list a URL is declared in, so the check does too: a
        // competitor indexed as a partner crawl would come back empty for both.
        var searched = new List<string?>();
        var (controller, _) = Build(AllIndexed, (runId, crawlType) =>
        {
            lock (searched) searched.Add(crawlType);
            return Holds(runId);
        });

        await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(1, searched.Count(t => t == CrawlTypes.ProjectSite));
        Assert.Equal(Partners.Length, searched.Count(t => t == CrawlTypes.Partner));
        Assert.Equal(Competitors.Length, searched.Count(t => t == CrawlTypes.Competitors));
    }

    [Fact]
    public async Task ASearchThatFailsRefusesTheSaveRatherThanJudgingTheUrl()
    {
        var (controller, repo) = Build(AllIndexed, (runId, _) => new GeekCrawlerRagQueryResult
        {
            RunId = runId, Pages = [], Failed = true, Error = "timeout",
        });

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("could not be searched", BodyOf(result), StringComparison.Ordinal);
        Assert.Contains("Nothing was saved", BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AUrlTheIndexDidNotAnswerForIsRefusedAndNamed()
    {
        // The index answered, but said nothing about this one. Same verdict as a red answer -- it
        // cannot be written from -- and the operator is told which URL.
        var (controller, repo) = Build(
            [.. AllIndexed.Where(r => !r.Url.Equals(Partner, StringComparison.OrdinalIgnoreCase))]);

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task ASixthPartnerWithNoCrawlIsExcludedRatherThanBlockingTheProject()
    {
        // The defect this exists to stop. The floor was counted on declared URLs while a separate
        // rule required every declared URL to be usable -- two rules over two different sets -- so
        // a sixth partner with no crawl disabled a project that already had five good ones. An
        // extra URL could only ever hurt.
        string[] sixPartners = [.. Partners, "https://partner6.test"];
        GeekCrawlerRagHostIndex[] rows = [.. AllIndexed, NotIndexed("https://partner6.test")];
        var (controller, repo) = Build(rows);

        var result = await controller.Create(
            CreateRequest(sixPartners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task TheExcludedPartnerIsNotSavedWithTheProject()
    {
        // Excluding it is the point rather than a side effect: a declared partner obliges Pillar,
        // Blog and Tool to name it (GccRequiredToolMentions), so saving one with no evidence behind
        // it only defers the refusal to generate time, where the operator can do nothing about it.
        string[] sixPartners = [.. Partners, "https://partner6.test"];
        GeekCrawlerRagHostIndex[] rows = [.. AllIndexed, NotIndexed("https://partner6.test")];
        var (controller, repo) = Build(rows);

        await controller.Create(CreateRequest(sixPartners, Competitors), CancellationToken.None);

        Assert.NotNull(repo.LastBody);
        Assert.DoesNotContain("partner6.test", repo.LastBody!, StringComparison.Ordinal);
        Assert.Contains("partner1.test", repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFloorIsMeasuredOnEvidenceSoFiveDeclaredWithOneBadIsStillRefused()
    {
        // The other half of the same rule, and the reason this is not simply a relaxation: five
        // declared with one unusable is four with evidence, which is below the floor and refused.
        var (controller, repo) = Build(AllIndexedExcept(Partner));

        var result = await controller.Create(
            CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("4 of 5", BodyOf(result), StringComparison.Ordinal);
        Assert.Contains(Partner, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AnUnusableSiteIsRefusedAndNeverMerelyExcluded()
    {
        // There is exactly one site and the project is grounded on its run, so nothing else can
        // stand in for it. Excluding it the way a sixth partner is excluded would save a project
        // with no grounding at all.
        string[] sixPartners = [.. Partners, "https://partner6.test"];
        GeekCrawlerRagHostIndex[] rows =
            [.. AllIndexedExcept(Site), NotIndexed("https://partner6.test")];
        var (controller, repo) = Build(rows);

        var result = await controller.Create(
            CreateRequest(sixPartners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Site, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task UpdateExcludesTheSameWay()
    {
        string[] sixPartners = [.. Partners, "https://partner6.test"];
        GeekCrawlerRagHostIndex[] rows = [.. AllIndexed, NotIndexed("https://partner6.test")];
        var (controller, repo) = Build(rows);

        await controller.Update(
            Guid.NewGuid(), UpdateRequest(sixPartners, Competitors), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
        Assert.NotNull(repo.LastBody);
        Assert.DoesNotContain("partner6.test", repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateIsGatedToo()
    {
        // The route a form gate cannot cover: updateProject has no call site in the UI, so PUT is
        // reachable only by direct API call.
        var (controller, repo) = Build(AllIndexedExcept(Competitor));

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains(Competitor, BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task UpdateWithEveryUrlIndexedReachesTheRepository()
    {
        var (controller, repo) = Build(AllIndexed);

        await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task UpdateRefusesAUrlWithNoAnswerToo()
    {
        var (controller, repo) = Build();

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }
}
