using System.Net;
using System.Security.Cryptography;
using GeekAPI.HttpClients;
using GeekApplication.Security;
using GeekRepository.Auth;
using GeekRepository.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.Tests.Security;

/// <summary>
/// GeekRepository answers only GeekAPI, by one credential: the token GeekAPI signs (Jeff, 2026-11-23). The host here is the real
/// AddGeekRepositoryAuth and the real policy on a repository route and a receptionist route, with no database. No key header opens
/// either route, whatever it says, and a bad token is a refusal on both. The public key is a process-wide environment variable, so
/// the tests in this class run one after another.
/// </summary>
[Collection("RepositoryKeyEnvironment")]
public sealed class GeekRepositoryAuthPipelineTests
{
    private static GeekRepositoryTokenSigner NewSigner()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Environment.SetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        return new GeekRepositoryTokenSigner(key, TimeProvider.System);
    }

    private static async Task<WebApplication> StartAsync()
    {
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

    private static async Task<HttpStatusCode> GetAsync(WebApplication app, string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        using var response = await app.GetTestClient().SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_token_GeekAPI_signed_opens_both_route_sets()
    {
        var signer = NewSigner();
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/repo/ping", ("Authorization", $"Bearer {signer.Create()}")));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/internal/v1/ping", ("Authorization", $"Bearer {signer.Create()}")));
    }

    [Theory]
    [InlineData("/repo/ping")]
    [InlineData("/internal/v1/ping")]
    public async Task A_request_with_no_token_is_refused_on_every_route(string path)
    {
        NewSigner();
        await using var app = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, path));
    }

    [Theory]
    [InlineData("X-Repo-Key")]
    [InlineData("X-Internal-Api-Key")]
    public async Task The_old_key_headers_open_nothing(string header)
    {
        NewSigner();
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/repo/ping", (header, "any-value")));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping", (header, "any-value")));
    }

    [Fact]
    public async Task A_token_presented_twice_is_refused_the_second_time()
    {
        var signer = NewSigner();
        await using var app = await StartAsync();
        var header = $"Bearer {signer.Create()}";

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/repo/ping", ("Authorization", header)));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/repo/ping", ("Authorization", header)));
    }

    [Theory]
    [InlineData("Bearer not-a-token")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    [InlineData("")]
    public async Task A_bad_or_wrong_kind_of_Authorization_header_is_refused_even_with_key_headers(string authorization)
    {
        NewSigner();
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/repo/ping",
            ("Authorization", authorization), ("X-Repo-Key", "any-value")));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/internal/v1/ping",
            ("Authorization", authorization), ("X-Internal-Api-Key", "any-value")));
    }

    [Fact]
    public async Task A_token_signed_by_another_key_is_refused()
    {
        NewSigner();
        var stranger = new GeekRepositoryTokenSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256), TimeProvider.System);
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/repo/ping", ("Authorization", $"Bearer {stranger.Create()}")));
    }

    [Fact]
    public void Startup_refuses_to_run_without_the_public_key_or_with_one_that_is_not_p256()
    {
        Environment.SetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable, null);
        var missing = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGeekRepositoryAuth());
        Assert.Contains(GeekRepositoryToken.PublicKeyVariable, missing.Message, StringComparison.Ordinal);

        Environment.SetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable, "not base64 !!");
        var malformed = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGeekRepositoryAuth());
        Assert.Contains(GeekRepositoryToken.PublicKeyVariable, malformed.Message, StringComparison.Ordinal);

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Environment.SetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable, Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()));
        var wrongCurve = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGeekRepositoryAuth());
        Assert.Contains("P-256", wrongCurve.Message, StringComparison.Ordinal);

        Environment.SetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable, null);
    }

    [Fact]
    public void GeekAPI_refuses_to_start_without_its_signing_key()
    {
        Environment.SetEnvironmentVariable(GeekRepositoryToken.SigningKeyVariable, null);
        var failure = Assert.Throws<InvalidOperationException>(() => GeekRepositoryTokenSigner.FromEnvironment(TimeProvider.System));
        Assert.Contains(GeekRepositoryToken.SigningKeyVariable, failure.Message, StringComparison.Ordinal);
    }
}
