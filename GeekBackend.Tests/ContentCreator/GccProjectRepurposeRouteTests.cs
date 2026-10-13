using System.Net;
using System.Reflection;
using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using ApiProjectsController = GeekAPI.Controllers.ContentCreator.GccProjectsController;
using Repo = GeekBackend.Tests.ContentCreator.GccProjectGenerateRouteTests.Repo;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Repurpose, through its real route: POST projects/{id}/pages/{pageId}/repurpose starts a run from one
/// approved page of the project, behind the gates a Generate is held to, or refuses having started
/// nothing. The route it replaced answered 403, was given no Brief and never asked whether the
/// project's site was crawled.
/// </summary>
public sealed class GccProjectRepurposeRouteTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly ApiProjectsController.RepurposeRequest EmailAndSocial =
        new(["email-cold-outreach", "social"], "OpenAi");

    [Fact]
    public void Both_routes_are_on_the_project_under_the_manage_policy()
    {
        var start = typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.Repurpose))!;
        var latest = typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.LatestRepurpose))!;

        Assert.Equal(
            GeekAPI.Auth.ContentCreatorAuthConstants.ManagePolicy,
            typeof(ApiProjectsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(start.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(latest.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("{id:guid}/pages/{pageId:guid}/repurpose", start.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:guid}/pages/{pageId:guid}/repurpose/latest", latest.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task An_approved_page_starts_a_run_written_from_it()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 6);
        var backing = GccProjectGenerateRouteTests.Create(project.Id);
        var page = Page(project.Id);
        var repo = new Repo(project, backing) { Page = page, PageBody = Body };

        var result = await GccProjectGenerateRouteTests.Controller(repo)
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(accepted.Value, Web));
        Assert.Equal(project.Id, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal(page.Id, body.RootElement.GetProperty("pageId").GetGuid());
        Assert.Equal("running", body.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.TryGetProperty("createId", out _));

        // The run row names the page, the pieces asked for and the brief the request read.
        var start = Assert.Single(repo.Starts);
        Assert.Equal(page.Id, start.SourcePageId);
        Assert.Equal(["email-cold-outreach", "social"], start.RequestedTypes);
        Assert.Equal(6, start.ExpectedBriefVersion);
        Assert.Equal(backing.Id, start.CreateId);
        Assert.Equal(body.RootElement.GetProperty("jobId").GetGuid(), start.Id);
        Assert.Equal(["start"], repo.Writes);

        // The test host registers no generate service, so the run fails in the background, and that
        // failure lands on its row.
        Assert.Equal(start.Id, await repo.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("in_review")]
    public async Task A_page_that_is_not_approved_is_refused_and_nothing_starts(string status)
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id) with { Status = status };
        var repo = new Repo(project, backing: null) { Page = page, PageBody = Body };

        var result = await GccProjectGenerateRouteTests.Controller(repo)
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Refused: approve the page before Repurpose. A piece is written from a page that has been approved.",
            (string)refused.Value!);
        Assert.Empty(repo.Starts);
        Assert.Empty(repo.Writes);
    }

    /// <summary>The gate the old route never had: a project whose site the index holds nothing for.</summary>
    [Fact]
    public async Task A_project_whose_site_is_not_crawled_starts_nothing()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var repo = new Repo(project, backing: null) { Page = page, PageBody = Body };

        var result = await GccProjectGenerateRouteTests
            .Controller(repo, GccProjectGenerateRouteTests.Index(project, project.SiteUrl!))
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.StartsWith("Nothing was started", (string)conflict.Value!);
        Assert.Contains(project.SiteUrl!, (string)conflict.Value!);
        Assert.Empty(repo.Starts);
    }

    /// <summary>The other gate: the Brief, which the old route was never given.</summary>
    [Fact]
    public async Task An_incomplete_brief_is_refused_naming_what_is_missing()
    {
        var project = GccProjectGenerateRouteTests.Project("""{"angle":"problem_solution"}""", briefVersion: 1);
        var page = Page(project.Id);
        var repo = new Repo(project, backing: null) { Page = page, PageBody = Body };

        var result = await GccProjectGenerateRouteTests.Controller(repo)
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        Assert.StartsWith("brief required: missing", (string)Assert.IsType<BadRequestObjectResult>(result).Value!);
        Assert.Empty(repo.Starts);
    }

    [Fact]
    public async Task A_page_that_is_not_this_projects_is_a_404_and_so_is_one_that_does_not_exist()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var elsewhere = Page(Guid.NewGuid());
        var repo = new Repo(project, backing: null) { Page = elsewhere, PageBody = Body };
        var controller = GccProjectGenerateRouteTests.Controller(repo);

        var anotherProjects = await controller.Repurpose(project.Id, elsewhere.Id, EmailAndSocial, CancellationToken.None);
        var noSuchPage = await controller.Repurpose(project.Id, Guid.NewGuid(), EmailAndSocial, CancellationToken.None);
        var noSuchProject = await GccProjectGenerateRouteTests.Controller(new Repo(project: null, backing: null))
            .Repurpose(Guid.NewGuid(), elsewhere.Id, EmailAndSocial, CancellationToken.None);

        Assert.IsType<NotFoundResult>(anotherProjects);
        Assert.IsType<NotFoundResult>(noSuchPage);
        Assert.IsType<NotFoundResult>(noSuchProject);
        Assert.Empty(repo.Starts);
    }

    [Theory]
    [InlineData("pillar", "Refused: Repurpose writes an email, a social post or an ad from a page; 'pillar' is not one of those. Nothing was written.")]
    [InlineData("", "Refused: name at least one piece to write. Repurpose has no default.")]
    public async Task Only_a_short_piece_can_be_asked_for(string type, string refusal)
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var repo = new Repo(project, backing: null) { Page = page, PageBody = Body };

        var result = await GccProjectGenerateRouteTests.Controller(repo).Repurpose(
            project.Id, page.Id,
            new ApiProjectsController.RepurposeRequest(type.Length == 0 ? [] : [type], "OpenAi"),
            CancellationToken.None);

        Assert.Equal(refusal, (string)Assert.IsType<BadRequestObjectResult>(result).Value!);
        Assert.Empty(repo.Starts);
    }

    [Fact]
    public async Task What_a_piece_is_written_from_is_a_pillar_a_blog_or_a_tool_page_with_text()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);

        async Task<string> RefusalFor(GccArtifactDto page, string? pageBody)
        {
            var repo = new Repo(project, backing: null) { Page = page, PageBody = pageBody };
            var result = await GccProjectGenerateRouteTests.Controller(repo)
                .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);
            Assert.Empty(repo.Starts);
            return (string)Assert.IsType<BadRequestObjectResult>(result).Value!;
        }

        // A piece written from a page is not itself a page to write from.
        Assert.Equal(
            "Refused: Repurpose writes from an approved pillar, blog or tool page, and this is a piece written from one.",
            await RefusalFor(Page(project.Id) with { Type = "social", ParentArtifactId = Guid.NewGuid() }, Body));
        Assert.Equal(
            "Refused: Repurpose writes from an approved pillar, blog or tool page; this page is a imagePrompt.",
            await RefusalFor(Page(project.Id) with { Type = "imagePrompt" }, Body));
        Assert.Equal(
            "Refused: the page has no text to write from.",
            await RefusalFor(Page(project.Id), pageBody: null));
        Assert.Equal(
            "Refused: the page has no text to write from.",
            await RefusalFor(Page(project.Id), """{"prompt":"a ledger on a desk"}"""));
    }

    [Fact]
    public async Task A_run_already_going_on_the_project_is_named_as_what_it_is()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var generate = GccProjectGenerateRouteTests.Job(project.Id);
        var repurpose = GccProjectGenerateRouteTests.Job(project.Id) with { SourcePageId = page.Id, RequestedTypes = ["social"] };

        async Task<string> ConflictWhen(GccGenerateJobDto running)
        {
            var repo = new Repo(project, backing: null)
            {
                Page = page, PageBody = Body, StartAnswer = GccGenerateJobStartResult.Running(running),
            };
            var result = await GccProjectGenerateRouteTests.Controller(repo)
                .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);
            return (string)Assert.IsType<ConflictObjectResult>(result).Value!;
        }

        var duringGenerate = await ConflictWhen(generate);
        Assert.StartsWith("A Generate is already running on this project", duringGenerate);
        Assert.Contains(generate.Id.ToString(), duringGenerate);

        var duringRepurpose = await ConflictWhen(repurpose);
        Assert.StartsWith("A Repurpose is already running on this project", duringRepurpose);
        Assert.Contains(repurpose.Id.ToString(), duringRepurpose);
    }

    /// <summary>
    /// The page was approved when this request read it and deleted before the run row was written -- a
    /// Generate that finished in between. The repository refuses the start, and the answer is the page's.
    /// </summary>
    [Fact]
    public async Task A_page_deleted_between_the_read_and_the_start_is_a_404_saying_so()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var repo = new Repo(project, backing: null)
        {
            Page = page, PageBody = Body, StartAnswer = GccGenerateJobStartResult.NoSourcePage(),
        };

        var result = await GccProjectGenerateRouteTests.Controller(repo)
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        Assert.Equal(
            "The page is no longer one of this project's pages. Nothing was started.",
            (string)Assert.IsType<NotFoundObjectResult>(result).Value!);
    }

    [Fact]
    public async Task A_brief_saved_after_the_read_is_refused()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var repo = new Repo(project, backing: null)
        {
            Page = page, PageBody = Body, StartAnswer = GccGenerateJobStartResult.Stale(),
        };

        var result = await GccProjectGenerateRouteTests.Controller(repo)
            .Repurpose(project.Id, page.Id, EmailAndSocial, CancellationToken.None);

        var conflict = (string)Assert.IsType<ConflictObjectResult>(result).Value!;
        Assert.StartsWith("The brief was saved again", conflict);
        Assert.Contains("reload and repurpose again", conflict);
    }

    // ---- Generate no longer writes a short piece ---------------------------------------------------

    [Theory]
    [InlineData("email-cold-outreach")]
    [InlineData("social")]
    [InlineData("ads")]
    [InlineData("linkedin")]
    public async Task A_generate_asked_for_a_short_piece_is_refused_naming_repurpose(string type)
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var repo = new Repo(project, backing: null);

        var result = await GccProjectGenerateRouteTests.Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar", type], "OpenAi"), CancellationToken.None);

        Assert.Equal(
            $"Refused: '{type}' is written by Repurpose from an approved pillar, blog or tool page, not by Generate. "
                + "Nothing was generated.",
            (string)Assert.IsType<BadRequestObjectResult>(result).Value!);
        Assert.Empty(repo.Starts);
    }

    // ---- the newest Repurpose of a page, read back -------------------------------------------------

    [Fact]
    public async Task The_newest_repurpose_of_a_page_is_read_back_apart_from_the_projects_generate()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);
        var page = Page(project.Id);
        var generate = GccProjectGenerateRouteTests.Job(project.Id);
        var repurpose = GccProjectGenerateRouteTests.Job(project.Id) with
        {
            SourcePageId = page.Id, RequestedTypes = ["social"], Status = GccGenerateJobStatuses.Ready,
            ResultJson = """{"created":[],"refusals":[],"preflight":[],"warnings":[]}""",
        };
        var controller = GccProjectGenerateRouteTests.Controller(
            new Repo(project, backing: null) { LatestJob = generate, LatestRepurpose = repurpose });

        var ofThePage = await controller.LatestRepurpose(project.Id, page.Id, CancellationToken.None);
        var ofTheProject = await controller.LatestGenerate(project.Id, CancellationToken.None);

        var pageRun = Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(ofThePage).Value).Run!;
        Assert.Equal(repurpose.Id, pageRun.JobId);
        Assert.Equal(page.Id, pageRun.SourcePageId);
        Assert.Equal(["social"], pageRun.RequestedTypes);
        Assert.Equal(repurpose.ResultJson, pageRun.ResultJson);

        // The project's own read is still its Generate.
        var projectRun = Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(ofTheProject).Value).Run!;
        Assert.Equal(generate.Id, projectRun.JobId);
        Assert.Null(projectRun.SourcePageId);
    }

    [Fact]
    public async Task A_page_never_repurposed_answers_with_no_run_and_a_missing_project_with_404()
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1);

        var never = await GccProjectGenerateRouteTests.Controller(new Repo(project, backing: null))
            .LatestRepurpose(project.Id, Guid.NewGuid(), CancellationToken.None);
        var missing = await GccProjectGenerateRouteTests.Controller(new Repo(project: null, backing: null))
            .LatestRepurpose(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(Assert.IsType<ApiProjectsController.LatestGenerateResponse>(Assert.IsType<OkObjectResult>(never).Value).Run);
        Assert.IsType<NotFoundResult>(missing);
    }

    // ---- fixtures ----------------------------------------------------------------------------------

    /// <summary>An approved pillar on a project.</summary>
    private static GccArtifactDto Page(Guid projectId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), projectId, null, "pillar", "AP: Approvals", "approved",
            DateTime.UtcNow, DateTime.UtcNow, 1, DateTime.UtcNow);

    /// <summary>The page's one version, as a long-form writer stores it: the document inside its envelope.</summary>
    private static readonly string Body = JsonSerializer.Serialize(
        new
        {
            title = "Approvals: a guide",
            body = new ContentDocument(
                new Section("p", "", [new TextParagraph([new Run("An invoice waits four days for a signature.")])], null, []),
                [new Section("h2", "Why approvals stall", [new TextParagraph([new Run("Routing by rule cut approval time by 60%.")])], null, [])]),
        },
        GccDocumentJson.Options);
}
