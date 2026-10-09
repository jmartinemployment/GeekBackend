using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.Controllers.Workflow.Hubs;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ApiProjectsController = GeekAPI.Controllers.ContentCreator.GccProjectsController;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GA2, GeekAPI half: POST projects/{id}/generate reads the project's brief and keyword, starts a run
/// row, and answers 202 with the project -- never the create -- or refuses having started nothing.
/// </summary>
public sealed class GccProjectGenerateRouteTests
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string CompleteBrief = """
        {"primaryIntent":"commercial","buyingStage":"consideration","audienceSegment":"AP managers",
         "audienceNotes":"mid-market","angle":"problem_solution","ctaType":"demo",
         "toneOfVoice":"plain","eeatSignals":["case study"],"lengthBand":"long"}
        """;

    [Fact]
    public void The_route_is_under_the_manage_policy()
    {
        var method = typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.Generate))!;

        Assert.Equal(
            ContentCreatorAuthConstants.ManagePolicy,
            typeof(ApiProjectsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("{id:guid}/generate", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task A_run_starts_on_the_projects_brief_answers_with_the_project_and_records_its_failure_on_its_row()
    {
        var project = Project(CompleteBrief, briefVersion: 4);
        var backing = Create(project.Id);
        var repo = new Repo(project, backing);

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "Anthropic"), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(accepted.Value, Web));
        Assert.Equal(project.Id, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal("running", body.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.TryGetProperty("createId", out _));

        var start = Assert.Single(repo.Starts);
        Assert.Equal(4, start.ExpectedBriefVersion);
        Assert.Equal(backing.Id, start.CreateId);
        Assert.Equal(["pillar"], start.RequestedTypes);
        Assert.Equal(body.RootElement.GetProperty("jobId").GetGuid(), start.Id);

        // The test host registers no generate service, so the run fails in the background -- and that
        // failure lands on its row rather than leaving it running.
        var failed = await repo.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(start.Id, failed);
    }

    [Fact]
    public async Task The_brief_needs_no_length_band_on_a_project()
    {
        var withoutBand = CompleteBrief.Replace(""","lengthBand":"long" """.Trim(), "");
        var project = Project(withoutBand, briefVersion: 1);
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Null(Assert.Single(repo.Starts).CreateId);
    }

    [Fact]
    public void The_writer_is_given_the_brief_without_a_length_band()
    {
        var stripped = ApiProjectsController.WithoutLengthBand(CompleteBrief)!;

        using var doc = JsonDocument.Parse(stripped);
        Assert.False(doc.RootElement.TryGetProperty("lengthBand", out _));
        Assert.Equal("problem_solution", doc.RootElement.GetProperty("angle").GetString());
    }

    [Fact]
    public void The_view_carries_the_projects_brief_and_keyword_and_the_creates_research_and_section()
    {
        var project = Project(CompleteBrief, briefVersion: 2);
        var backing = Create(project.Id) with
        {
            BriefJson = """{"angle":"stale create brief"}""",
            Topic = "Old keyword",
            ResearchJson = """{"serp":[]}""",
            SiteSectionJson = """{"relatedPages":["https://acme.test/ap"]}""",
        };

        var view = ApiProjectsController.ProjectView(project, backing.Id, Guid.Parse(Sub), "pillar", backing);

        Assert.Equal(backing.Id, view.Id);
        Assert.Equal("AP: Approvals", view.Topic);
        Assert.Contains("problem_solution", view.BriefJson);
        Assert.Equal("""{"serp":[]}""", view.ResearchJson);
        Assert.Equal(backing.SiteSectionJson, view.SiteSectionJson);
        Assert.Equal(project.Id, view.ProjectId);
        Assert.Equal(project.ProjectSiteRunId, view.ProjectSiteRunId);
    }

    [Fact]
    public async Task A_second_run_is_refused_naming_the_one_running()
    {
        var project = Project(CompleteBrief, briefVersion: 1);
        var running = Job(project.Id);
        var repo = new Repo(project, backing: null)
        {
            StartAnswer = GccGenerateJobStartResult.Running(running),
        };

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["blog"], "OpenAi"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains(running.Id.ToString(), (string)conflict.Value!);
    }

    [Fact]
    public async Task A_brief_saved_after_the_read_is_refused()
    {
        var project = Project(CompleteBrief, briefVersion: 1);
        var repo = new Repo(project, backing: null) { StartAnswer = GccGenerateJobStartResult.Stale() };

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.StartsWith("The brief was saved again", (string)conflict.Value!);
    }

    [Theory]
    [InlineData(null, "pillar", "Anthropic", "keyword required")]
    [InlineData("AP: Approvals", "pillar", null, "No provider was specified")]
    [InlineData("AP: Approvals", "", "Anthropic", null)]
    public async Task Refusals_before_the_run_start_nothing(string? topic, string type, string? provider, string? startsWith)
    {
        var project = Project(CompleteBrief, briefVersion: 1) with { Topic = topic };
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo).Generate(
            project.Id,
            new ApiProjectsController.GenerateRequest(type.Length == 0 ? [] : [type], provider),
            CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        if (startsWith is not null) Assert.StartsWith(startsWith, (string)refused.Value!);
        Assert.Empty(repo.Starts);
    }

    [Fact]
    public async Task An_incomplete_brief_is_refused_naming_what_is_missing()
    {
        var project = Project("""{"angle":"problem_solution"}""", briefVersion: 1);
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.StartsWith("brief required: missing", (string)refused.Value!);
        Assert.DoesNotContain("lengthBand", (string)refused.Value!);
        Assert.Empty(repo.Starts);
    }

    /// <summary>
    /// Validated when entered; asked once more before anything is spent, in case the index has lost a
    /// crawl since the Profile was saved.
    /// </summary>
    [Fact]
    public async Task A_declared_url_the_index_now_finds_nothing_for_stops_the_run_before_it_starts()
    {
        var project = Project(CompleteBrief, briefVersion: 1) with { CompetitorUrls = [Rival] };
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo, Index(project, Rival)).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.StartsWith("Nothing was started", (string)conflict.Value!);
        Assert.Contains(Rival, (string)conflict.Value!);
        Assert.Empty(repo.Starts);
    }

    /// <summary>
    /// 2026-10-05: the site was re-crawled, which deletes the earlier crawl. The project still pointed
    /// at the deleted one; the check before Generate passed on the new crawl; the run searched the dead
    /// one and was "written without it". The run is written from the crawl that was checked.
    /// </summary>
    [Fact]
    public async Task A_re_crawled_site_is_picked_up_before_the_run_starts()
    {
        var project = Project(CompleteBrief, briefVersion: 1);
        var newCrawl = Guid.NewGuid();
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo, Index(project, siteRun: newCrawl)).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var repointed = Assert.Single(repo.SiteRuns);
        Assert.Equal(newCrawl, repointed.ProjectSiteRunId);
        Assert.Equal(project.Id, repointed.ProjectId);
        Assert.Equal(Sub, repointed.ActorUserId);
        // The project points at the new crawl before the run exists, not after.
        Assert.Equal(["site-run", "start"], repo.Writes);
    }

    [Fact]
    public async Task A_site_that_has_not_been_re_crawled_changes_nothing_on_the_project()
    {
        var project = Project(CompleteBrief, briefVersion: 1);
        var repo = new Repo(project, backing: null);

        var result = await Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(repo.SiteRuns);
        Assert.Equal(["start"], repo.Writes);
    }

    [Fact]
    public async Task If_the_project_cannot_be_pointed_at_the_new_crawl_nothing_starts()
    {
        var project = Project(CompleteBrief, briefVersion: 1);
        var repo = new Repo(project, backing: null) { SiteRunAnswer = HttpStatusCode.Conflict };

        var result = await Controller(repo, Index(project, siteRun: Guid.NewGuid())).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("Nothing was started", (string)conflict.Value!);
        Assert.Empty(repo.Starts);
    }

    [Fact]
    public async Task A_missing_project_is_a_404()
    {
        var repo = new Repo(project: null, backing: null);

        var result = await Controller(repo).Generate(
            Guid.NewGuid(), new ApiProjectsController.GenerateRequest(["pillar"], "OpenAi"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void A_version_written_by_a_project_run_names_the_brief_revision_it_came_from()
    {
        var revision = new GccBriefRevisionStamp(Guid.NewGuid(), new DateTime(2026, 10, 4, 14, 32, 0, DateTimeKind.Utc));

        using var stamped = JsonDocument.Parse(GccVersionProvenance.For(ContentGeneratorProvider.Anthropic, revision));
        using var plain = JsonDocument.Parse(GccVersionProvenance.For(ContentGeneratorProvider.Anthropic));

        Assert.Equal(revision.Id, stamped.RootElement.GetProperty("briefRevisionId").GetGuid());
        Assert.Equal(revision.SavedAtUtc, stamped.RootElement.GetProperty("briefRevisionSavedAtUtc").GetDateTime());
        Assert.Equal("Anthropic", stamped.RootElement.GetProperty("generatedByProvider").GetString());
        Assert.False(plain.RootElement.TryGetProperty("briefRevisionId", out _));
    }

    [Fact]
    public void A_project_runs_event_names_the_project_and_not_the_create()
    {
        var projectId = Guid.NewGuid();
        var job = new GccJob(Guid.NewGuid(), "generate", Guid.NewGuid(), Sub, "running", null, null,
            DateTime.UtcNow, null, projectId);

        using var mapped = JsonDocument.Parse(JsonSerializer.Serialize(GccGenerateEventMapper.Map(job), Web));

        Assert.Equal(projectId, mapped.RootElement.GetProperty("projectId").GetGuid());
        Assert.False(mapped.RootElement.TryGetProperty("createId", out _));
    }

    private const string Rival = "https://rival.test";

    /// <summary>The index: every declared URL has a finished crawl, and a search of its run finds pages
    /// unless the URL is one of <paramref name="emptyUrls"/>.</summary>
    private static GccDeclaredUrlValidator Index(GccProjectDto project, params string[] emptyUrls) =>
        Index(project, siteRun: null, emptyUrls);

    /// <param name="siteRun">The crawl of the project's site the index holds. Null means the one the
    /// project already points at -- a site nobody has re-crawled since the Profile was saved.</param>
    private static GccDeclaredUrlValidator Index(GccProjectDto project, Guid? siteRun, params string[] emptyUrls)
    {
        var rows = new[] { project.SiteUrl! }.Concat(project.PartnerUrls).Concat(project.CompetitorUrls)
            .Select(u => new GeekCrawlerRagHostIndex(
                u, new Uri(u).Host, true,
                (u == project.SiteUrl ? siteRun ?? project.ProjectSiteRunId ?? Guid.NewGuid() : Guid.NewGuid()).ToString()))
            .ToList();
        var emptyRuns = rows.Where(r => emptyUrls.Contains(r.Url)).Select(r => Guid.Parse(r.RunId!)).ToHashSet();
        var crawlerRepo = new HttpGeekCrawlerRepository(
            new HttpClient(new DeclaredUrlTestDoubles.UsableRunHandler()) { BaseAddress = new Uri("https://crawler.test") },
            NullLogger<HttpGeekCrawlerRepository>.Instance);
        return new GccDeclaredUrlValidator(
            new GccCompetitorAnalysisResolverTests.FakeRag(rows, (runId, _) => emptyRuns.Contains(runId)
                ? DeclaredUrlTestDoubles.HoldsNothing(runId)
                : DeclaredUrlTestDoubles.Holds(runId)),
            crawlerRepo,
            NullLogger<GccDeclaredUrlValidator>.Instance);
    }

    // ---- the newest run, read back -----------------------------------------------------------------

    [Fact]
    public async Task A_running_run_is_read_back_with_the_id_the_page_joins_and_nothing_internal()
    {
        // Reload during a run and the page showed nothing running: it had only ever learned of a run
        // from the click that started it.
        var project = Project("""{"angle":"problem_solution"}""", briefVersion: 3);
        var running = Job(project.Id) with { RequestedTypes = ["pillar", "tool"] };
        var repo = new Repo(project, backing: null) { LatestJob = running };

        var result = await Controller(repo).LatestGenerate(project.Id, CancellationToken.None);

        var body = Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(running.Id, body.Run!.JobId);
        Assert.Equal(GccGenerateJobStatuses.Running, body.Run.Status);
        Assert.Equal(["pillar", "tool"], body.Run.RequestedTypes);
        Assert.Equal(running.StartedAtUtc, body.Run.StartedAtUtc);
        Assert.Null(body.Run.FinishedAtUtc);

        // Which create the drafts are stored under, and who started the run, are not the page's.
        var json = JsonSerializer.Serialize(body, Web);
        Assert.DoesNotContain(running.CreateId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(repo.Writes);
    }

    [Fact]
    public async Task An_ended_run_is_read_back_with_what_it_recorded()
    {
        // Reload after a run and what it refused, and why, was gone: it had only ever been on the hub.
        var project = Project("""{"angle":"problem_solution"}""", briefVersion: 3);
        var finishedAt = new DateTime(2026, 10, 5, 18, 32, 7, DateTimeKind.Utc);
        const string recorded = """{"created":[],"refusals":["Approvalmax: Refused: money that is not in US dollars"],"preflight":[],"warnings":[]}""";
        var ended = Job(project.Id) with
        {
            Status = GccGenerateJobStatuses.Ready, ResultJson = recorded, FinishedAtUtc = finishedAt,
        };

        var result = await Controller(new Repo(project, backing: null) { LatestJob = ended })
            .LatestGenerate(project.Id, CancellationToken.None);

        var run = Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(result).Value).Run!;
        Assert.Equal(GccGenerateJobStatuses.Ready, run.Status);
        Assert.Equal(finishedAt, run.FinishedAtUtc);
        Assert.Equal(recorded, run.ResultJson);
        Assert.Null(run.Error);
    }

    [Fact]
    public async Task A_project_that_has_never_run_answers_with_no_run_and_a_missing_project_with_404()
    {
        var project = Project("""{"angle":"problem_solution"}""", briefVersion: 3);

        var never = await Controller(new Repo(project, backing: null)).LatestGenerate(project.Id, CancellationToken.None);
        var missing = await Controller(new Repo(project: null, backing: null)).LatestGenerate(Guid.NewGuid(), CancellationToken.None);

        // An object with a null in it, not an empty 204: "never run" is an answer the page reads.
        Assert.Null(Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(never).Value).Run);
        Assert.IsType<NotFoundResult>(missing);
    }

    [Fact]
    public void The_latest_run_is_read_under_the_manage_policy_like_the_rest_of_the_project()
    {
        Assert.Equal(
            "{id:guid}/generate/latest",
            typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.LatestGenerate))!
                .GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template);
    }

    private static ApiProjectsController Controller(Repo repo, GccDeclaredUrlValidator? index = null)
    {
        var http = new HttpGccRepository(
            new HttpClient(repo) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        // The background run resolves its services from this provider: the repository only.
        var services = new ServiceCollection().AddSingleton(http).BuildServiceProvider();
        var runner = new GccGenerateJobRunner(
            new GccJobStore(),
            new GccGenerateNotifier(new SilentHub()),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GccGenerateJobRunner>.Instance);
        // A site run with no crawled pages: the must-mention read runs and finds nothing to add.
        var mustMention = new GccMustMentionBlockBuilder(
            new GccProjectSiteStructureReader(new NoPages()), NullLogger<GccMustMentionBlockBuilder>.Instance);
        var controller = new ApiProjectsController(
            // With no project the route returns before the index is asked, so none is built.
            http, index ?? (repo.Project is null ? null! : Index(repo.Project)), runner, mustMention,
            NullLogger<ApiProjectsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Sub)], "test")),
                },
            },
        };
        return controller;
    }

    private sealed class NoPages : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    /// <summary>GeekRepository, as the routes the generate path calls.</summary>
    private sealed class Repo(GccProjectDto? project, GccCreateDto? backing) : HttpMessageHandler
    {
        public GccProjectDto? Project => project;
        public List<StartGccGenerateJobCommand> Starts { get; } = [];

        /// <summary>Every call that changes something, in the order it arrived.</summary>
        public List<string> Writes { get; } = [];
        public List<SetGccProjectSiteRunCommand> SiteRuns { get; } = [];
        public HttpStatusCode SiteRunAnswer { get; init; } = HttpStatusCode.OK;
        public TaskCompletionSource<Guid> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GccGenerateJobStartResult? StartAnswer { get; init; }

        /// <summary>The project's newest run as GeekRepository holds it, or null when it has never run.</summary>
        public GccGenerateJobDto? LatestJob { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/backing-create"))
                return backing is null ? Status(HttpStatusCode.NotFound) : Json(backing);
            if (request.Method == HttpMethod.Get && path.EndsWith("/generate-jobs/latest"))
                return LatestJob is null ? Status(HttpStatusCode.NotFound) : Json(LatestJob);
            if (request.Method == HttpMethod.Get && path.StartsWith("/repo/content-creator/projects/"))
                return project is null ? Status(HttpStatusCode.NotFound) : Json(project);
            if (request.Method == HttpMethod.Post && path == "/repo/content-creator/generate-jobs")
            {
                var start = JsonSerializer.Deserialize<StartGccGenerateJobCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                Starts.Add(start);
                Writes.Add("start");
                return Json(StartAnswer ?? GccGenerateJobStartResult.Started(
                    Job(start.ProjectId) with { Id = start.Id, CreateId = start.CreateId ?? Guid.NewGuid() }));
            }
            if (request.Method == HttpMethod.Put && path.EndsWith("/site-run"))
            {
                var command = JsonSerializer.Deserialize<SetGccProjectSiteRunCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                SiteRuns.Add(command);
                Writes.Add("site-run");
                return SiteRunAnswer == HttpStatusCode.OK
                    ? Json(project! with { ProjectSiteRunId = command.ProjectSiteRunId })
                    : Status(SiteRunAnswer);
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/fail"))
            {
                var id = Guid.Parse(path.Split('/')[^2]);
                Failed.TrySetResult(id);
                return Json(Job(project!.Id) with { Id = id, Status = GccGenerateJobStatuses.Failed });
            }
            return Status(HttpStatusCode.NotImplemented);
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value, Web), Encoding.UTF8, "application/json"),
            };

        private static HttpResponseMessage Status(HttpStatusCode code) => new(code);
    }

    private sealed class SilentHub : IHubContext<WorkflowRealtimeHub>
    {
        public IHubClients Clients { get; } = new SilentClients();
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class SilentClients : IHubClients
        {
            private static readonly IClientProxy Proxy = new SilentProxy();
            public IClientProxy All => Proxy;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
            public IClientProxy Client(string connectionId) => Proxy;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
            public IClientProxy Group(string groupName) => Proxy;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
            public IClientProxy User(string userId) => Proxy;
            public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
        }

        private sealed class SilentProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }

    private static GccProjectDto Project(string brief, int briefVersion) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Q4", null, null, GccProjectStatuses.Planned, "https://acme.test",
            Guid.NewGuid(), null, [], [], DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow, brief, "AP: Approvals", null, null, briefVersion, DateTime.UtcNow);

    private static GccCreateDto Create(Guid projectId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.Parse(Sub), "tool", "AP", null, null, null, null, null,
            "draft", DateTime.UtcNow, DateTime.UtcNow, "marketing", projectId);

    private static GccGenerateJobDto Job(Guid projectId) =>
        new(Guid.NewGuid(), projectId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, Sub, ["pillar"],
            "Anthropic", GccGenerateJobStatuses.Running, null, null, DateTime.UtcNow, null);
}
