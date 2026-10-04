using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ApiProjectsController = GeekAPI.Controllers.ContentCreator.GccProjectsController;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// GA1: PATCH projects/{id}/brief on GeekAPI -- the actor is the token's subject, a stale read is a
/// 409 with nothing written, and the response is the shape the frontend's contract names.
/// </summary>
public sealed class GccProjectBriefRouteTests
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";

    private sealed class RepoStub(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public List<string> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void The_route_is_under_the_manage_policy_and_nothing_opens_it()
    {
        var method = typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.SaveBrief))!;

        Assert.Equal(
            ContentCreatorAuthConstants.ManagePolicy,
            typeof(ApiProjectsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("{id:guid}/brief", method.GetCustomAttribute<HttpPatchAttribute>()!.Template);
    }

    [Fact]
    public async Task A_save_returns_the_new_version_and_the_revision_and_attributes_it_to_the_token()
    {
        var projectId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var savedAt = new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc);
        var repoAnswer = JsonSerializer.Serialize(
            GccProjectBriefSaveResult.Saved(
                Project(projectId, version: 124, topic: "AP: Approvals"),
                new GccProjectRevisionDto(revisionId, projectId, "manual", "{}", "AP: Approvals", Sub, savedAt)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var stub = new RepoStub(HttpStatusCode.OK, repoAnswer);

        var result = await Controller(stub).SaveBrief(
            projectId, new ApiProjectsController.SaveBriefRequest("{}", "AP: Approvals", 123), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new ApiProjectsController.SaveBriefResponse(124, revisionId, savedAt, "AP: Approvals"), ok.Value);
        using var sent = JsonDocument.Parse(Assert.Single(stub.Sent));
        Assert.Equal(Sub, sent.RootElement.GetProperty("actorUserId").GetString());
        Assert.Equal(123, sent.RootElement.GetProperty("expectedVersion").GetInt32());
    }

    [Fact]
    public async Task A_stale_save_is_a_409_naming_why()
    {
        var result = await Controller(new RepoStub(HttpStatusCode.Conflict)).SaveBrief(
            Guid.NewGuid(), new ApiProjectsController.SaveBriefRequest("{}", null, 1), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.StartsWith("This project was changed after you loaded it", (string)conflict.Value!);
    }

    [Fact]
    public async Task A_missing_project_is_a_404()
    {
        var result = await Controller(new RepoStub(HttpStatusCode.NotFound)).SaveBrief(
            Guid.NewGuid(), new ApiProjectsController.SaveBriefRequest("{}", null, 1), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task A_brief_that_is_not_json_never_reaches_the_repository()
    {
        var stub = new RepoStub(HttpStatusCode.OK);

        var result = await Controller(stub).SaveBrief(
            Guid.NewGuid(), new ApiProjectsController.SaveBriefRequest("not json", null, 1), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(stub.Sent);
    }

    [Fact]
    public async Task No_subject_is_unauthorized_and_nothing_is_sent()
    {
        var stub = new RepoStub(HttpStatusCode.OK);

        var result = await Controller(stub, sub: null).SaveBrief(
            Guid.NewGuid(), new ApiProjectsController.SaveBriefRequest("{}", null, 1), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(stub.Sent);
    }

    private static ApiProjectsController Controller(RepoStub stub, string? sub = Sub)
    {
        var repo = new HttpGccRepository(
            new HttpClient(stub) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        // The brief route reaches none of the RAG client, the crawler repository or the generate runner.
        var controller = new ApiProjectsController(repo, null!, null!, null!, null!, NullLogger<ApiProjectsController>.Instance);
        var claims = sub is null ? Array.Empty<Claim>() : [new Claim("sub", sub)];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
        return controller;
    }

    private static GccProjectDto Project(Guid id, int version, string topic) =>
        new(id, Guid.NewGuid(), "Q4", null, null, GccProjectStatuses.Planned, null, null, null, [], [],
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow,
            "{}", topic, null, null, version);
}
