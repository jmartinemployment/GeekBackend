using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.Controllers.Workflow.Hubs;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
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

    private static ApiProjectsController Controller(Repo repo)
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
            // Generate does not validate declared URLs -- that is done when they are entered.
            http, null!, runner, mustMention, NullLogger<ApiProjectsController>.Instance)
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
        public List<StartGccGenerateJobCommand> Starts { get; } = [];
        public TaskCompletionSource<Guid> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GccGenerateJobStartResult? StartAnswer { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/backing-create"))
                return backing is null ? Status(HttpStatusCode.NotFound) : Json(backing);
            if (request.Method == HttpMethod.Get && path.StartsWith("/repo/content-creator/projects/"))
                return project is null ? Status(HttpStatusCode.NotFound) : Json(project);
            if (request.Method == HttpMethod.Post && path == "/repo/content-creator/generate-jobs")
            {
                var start = JsonSerializer.Deserialize<StartGccGenerateJobCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                Starts.Add(start);
                return Json(StartAnswer ?? GccGenerateJobStartResult.Started(
                    Job(start.ProjectId) with { Id = start.Id, CreateId = start.CreateId ?? Guid.NewGuid() }));
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
