using System.Net;
using GeekRepository.Auth;
using GeekRepository.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The two credentials in front of GeekRepository (docs/Geek-Receptionist.md section 1): /internal/v1/* answers only to
/// X-Internal-Api-Key and every other route only to X-Repo-Key, and neither stands in for the other. The host is the real
/// AddGeekRepositoryAuth and the real policy on two routes, with no database. The environment variables are process-wide, so the
/// tests in this class run one after another and put the variables back.
/// </summary>
[Collection("RepositoryKeyEnvironment")]
public sealed class VoiceInternalAuthTests
{
    private const string RepoKey = "repo-key-for-tests";
    private const string InternalKey = "internal-key-for-tests";

    private static async Task<WebApplication> StartAsync()
    {
        Environment.SetEnvironmentVariable("REPO_API_KEY", RepoKey);
        Environment.SetEnvironmentVariable("GEEK_REPOSITORY_API_KEY", InternalKey);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGeekRepositoryAuth();
        var app = builder.Build();
        app.UseGeekRepositoryAuth();
        app.MapGet("/repo/ping", () => "ok").RequireAuthorization(RepositoryAuthConstants.InternalServicePolicy);
        app.MapGet("/internal/v1/ping", () => "ok").RequireAuthorization(RepositoryAuthConstants.InternalServicePolicy);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> GetAsync(WebApplication app, string path, params (string Name, string[] Values)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, values) in headers) request.Headers.Add(name, values);
        using var response = await app.GetTestClient().SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Internal_route_without_a_key_is_refused()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping"));
    }

    [Fact]
    public async Task Internal_route_with_a_wrong_key_is_refused()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping", ("X-Internal-Api-Key", ["wrong-key"])));
    }

    [Fact]
    public async Task Internal_route_with_an_empty_key_is_refused()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping", ("X-Internal-Api-Key", [""])));
    }

    [Fact]
    public async Task Internal_route_with_the_key_repeated_is_refused()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping", ("X-Internal-Api-Key", [InternalKey, InternalKey])));
    }

    [Fact]
    public async Task Internal_route_does_not_answer_to_the_repository_key()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping", ("X-Repo-Key", [RepoKey])));
    }

    [Fact]
    public async Task Internal_route_answers_to_the_internal_key()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/internal/v1/ping", ("X-Internal-Api-Key", [InternalKey])));
    }

    [Fact]
    public async Task Existing_route_does_not_answer_to_the_internal_key()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/repo/ping", ("X-Internal-Api-Key", [InternalKey])));
    }

    [Fact]
    public async Task Existing_route_still_answers_to_the_repository_key()
    {
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/repo/ping", ("X-Repo-Key", [RepoKey])));
    }

    [Fact]
    public async Task Startup_refuses_to_run_without_the_internal_key()
    {
        Environment.SetEnvironmentVariable("REPO_API_KEY", RepoKey);
        Environment.SetEnvironmentVariable("GEEK_REPOSITORY_API_KEY", null);
        var services = new ServiceCollection();

        var failure = Assert.Throws<InvalidOperationException>(() => services.AddGeekRepositoryAuth());

        Assert.Contains("GEEK_REPOSITORY_API_KEY", failure.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }
}
