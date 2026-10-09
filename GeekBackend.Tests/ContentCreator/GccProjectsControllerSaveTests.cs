using System.Net;
using System.Security.Claims;
using System.Text;
using GeekAPI.Controllers.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A Profile save persists the URLs the operator declared, and does not ask the index about them.
///
/// <para>
/// Until 2026-10-09 the save was gated on index evidence -- a usable site, five usable partners, five
/// usable competitors, the rest dropped from what was saved -- so a URL whose crawl was still being
/// indexed, or an index that was briefly unreachable, made the Profile unsaveable. Jeff, 2026-10-09:
/// "Fix and enable project save when unindexed urls exist." Nothing is written from a URL at save
/// time. Whether it can be written from is the index's answer, shown on the form as it is entered and
/// asked again by Generate, which refuses until every declared URL is usable
/// (<c>GccProjectGenerateRouteTests</c>).
/// </para>
///
/// <para>
/// What the save still refuses is a project with no site URL, and a URL that is not a URL. The floor
/// of five partners and five competitors was lifted the same day (Jeff: "Create Project being disabled
/// wastes my time, disable this blocking"). Every refusal case asserts the repository was never called.
/// </para>
/// </summary>
public class GccProjectsControllerSaveTests
{
    private const string Site = "https://acme.test";

    private static string[] Partners => [.. Enumerable.Range(1, 5).Select(i => $"https://partner{i}.test")];
    private static string[] Competitors => [.. Enumerable.Range(1, 5).Select(i => $"https://rival{i}.test")];

    private static string Partner => Partners[0];

    /// <summary>The index's answer about every declared URL: no crawl is indexed for any of them.</summary>
    private static GeekCrawlerRagHostIndex[] NothingIndexed =>
        [.. new[] { Site }.Concat(Partners).Concat(Competitors)
            .Select(u => new GeekCrawlerRagHostIndex(u, new Uri(u).Host, false, null))];

    private static readonly Guid SiteRun = Guid.NewGuid();

    /// <summary>
    /// Stands in for the repository service. Records whether it was reached and what it was sent:
    /// "the save went through" and "the save wrote what the operator declared" are different claims,
    /// and only the body can tell them apart.
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

    private static (GccProjectsController Controller, RecordingHandler Repo, GccCompetitorAnalysisResolverTests.FakeRag Index) Build(
        params GeekCrawlerRagHostIndex[] rows)
    {
        var handler = new RecordingHandler();
        var repo = new HttpGccRepository(
            new HttpClient(handler) { BaseAddress = new Uri("https://repo.test") },
            NullLogger<HttpGccRepository>.Instance);

        var crawlerRepo = new HttpGeekCrawlerRepository(
            new HttpClient(new DeclaredUrlTestDoubles.UsableRunHandler()) { BaseAddress = new Uri("https://crawler.test") },
            NullLogger<HttpGeekCrawlerRepository>.Instance);

        // The index fake already written for GccCompetitorAnalysisResolverTests -- one implementation
        // of IGeekCrawlerRagClient for the suite. It records every hosts lookup, which is how these
        // tests assert the save made none.
        var index = new GccCompetitorAnalysisResolverTests.FakeRag(rows, (runId, _) => DeclaredUrlTestDoubles.Holds(runId));

        var controller = new GccProjectsController(
            repo,
            new GccDeclaredUrlValidator(index, crawlerRepo, NullLogger<GccDeclaredUrlValidator>.Instance),
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

        return (controller, handler, index);
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

    // ---- the save goes through, and saves what was declared --------------------------------------

    [Fact]
    public async Task DeclaredUrlsWithNoIndexedCrawlAreSaved()
    {
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
        Assert.NotNull(repo.LastBody);
        foreach (var url in Partners.Concat(Competitors).Append(Site))
            Assert.Contains(url, repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIndexIsNotAskedOnSave()
    {
        // Not "asked and ignored": not asked. A save that waits on the index is a save that fails
        // when the index does, and the answer has nothing to decide here.
        var (controller, _, index) = Build(NothingIndexed);

        await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Empty(index.HostsAskedAs);
    }

    [Fact]
    public async Task AnUnreachableIndexDoesNotStopTheSave()
    {
        // No rows at all is what HostsIndexedAsync returns when the Library is disabled, down or
        // erroring. It used to refuse every save with "could not be reached".
        var (controller, repo, _) = Build();

        var result = await controller.Create(CreateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task NoProjectSiteRunIdIsSaved()
    {
        // The run the project is grounded on is resolved from the index when Generate is pressed, and
        // the project is pointed at it then (GccProjectsController.Generate). The form sends one when
        // the index has already answered; a site whose crawl is still indexing has none to send.
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(
            CreateRequest(Partners, Competitors) with { ProjectSiteRunId = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task ASixthPartnerWithNoCrawlIsSavedWithTheOtherFive()
    {
        // Nothing is excluded. The old save dropped this one from the project without a word; now
        // Generate names it, where the operator can re-index it or remove it.
        string[] sixPartners = [.. Partners, "https://partner6.test"];
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest(sixPartners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Contains("partner6.test", repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameUrlInADifferentCaseIsSavedOnceInItsFirstSpelling()
    {
        string[] partners = [.. Partners, "https://PARTNER1.test"];
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest(partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Contains("https://partner1.test", repo.LastBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTNER1.test", repo.LastBody!, StringComparison.Ordinal);
    }

    // ---- any number of partners and competitors -----------------------------------------------------

    [Fact]
    public async Task OnePartnerAndOneCompetitorAreSaved()
    {
        // The floor of five each is gone (2026-10-09). A project is saved with what was declared.
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest([Partner], [Competitors[0]]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
        Assert.Contains(Partner, repo.LastBody!, StringComparison.Ordinal);
        Assert.Contains(Competitors[0], repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyListsAreSaved()
    {
        // Nothing is written from a partner at save time. A project with none yet is a project the
        // operator is still filling in; Generate is where a missing partner matters.
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest([], []), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    // ---- what the save still refuses: no site URL, and URL syntax ----------------------------------

    [Fact]
    public async Task NoSiteUrlIsRefused()
    {
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(
            CreateRequest(Partners, Competitors) with { SiteUrl = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("Project site URL: 0 declared, 1 required", BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task AnInvalidUrlIsStillRefused()
    {
        string[] partners = [.. Partners.Take(4), "not a url"];
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Create(CreateRequest(partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("partnerUrls contains an invalid URL", BodyOf(result), StringComparison.Ordinal);
        Assert.Equal(0, repo.Calls);
    }

    // ---- Update, the same way ----------------------------------------------------------------------

    [Fact]
    public async Task UpdateSavesDeclaredUrlsWithoutAskingTheIndex()
    {
        var (controller, repo, index) = Build(NothingIndexed);

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
        Assert.Empty(index.HostsAskedAs);
        foreach (var url in Partners.Concat(Competitors))
            Assert.Contains(url, repo.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateWithNoProjectSiteRunIdIsSaved()
    {
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors) with { ProjectSiteRunId = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task UpdateWithAnUnreachableIndexIsSaved()
    {
        var (controller, repo, _) = Build();

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task UpdateSavesAnyNumberOfPartnersToo()
    {
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest([Partner], []), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task UpdateWithNoSiteUrlIsRefused()
    {
        var (controller, repo, _) = Build(NothingIndexed);

        var result = await controller.Update(
            Guid.NewGuid(), UpdateRequest(Partners, Competitors) with { SiteUrl = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repo.Calls);
    }
}
