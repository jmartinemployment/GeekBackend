using GeekAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeekBackend.Tests.Middleware;

/// <summary>
/// In Production an unsigned bearer identity is rejected and never reaches the next delegate.
/// </summary>
/// <remarks>
/// <para>
/// Restored 2026-10-03. This assertion lived inside <c>GccV2GovernedSkillsTests.cs</c> and went with
/// that file in <c>fce86ad</c> — because the FILE was named after the skills subsystem, not because its
/// subject had been deleted. Its subject is <see cref="ApiKeyMiddleware"/>, which guards every route in
/// the API and was never touched.
/// </para>
/// <para>
/// It was the only test anywhere referencing the middleware. Afterwards
/// <c>grep -rln ApiKeyMiddleware GeekBackend.Tests/</c> returned nothing, so a change making it accept
/// an unsigned bearer would have passed all 1,512 tests. Code review found that; the suite could not.
/// </para>
/// <para>
/// It lives beside its subject now rather than inside a feature's test file, which is what let it be
/// swept as collateral. When a deletion is scoped by filename, a test's home decides whether it
/// survives.
/// </para>
/// </remarks>
public class ApiKeyMiddlewareAuthBoundaryTests
{
    [Theory]
    // A bare GUID: an identity claimed with no signature at all.
    [InlineData("11111111-1111-1111-1111-111111111111")]
    // alg:none -- a JWT whose own header declares it unsigned, which is the classic forgery.
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiIxMTExMTExMS0xMTExLTExMTEtMTExMS0xMTExMTExMTExMTEifQ.")]
    public async Task Production_rejects_an_unsigned_bearer_identity(string token)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            new TestEnvironment("Production"));

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/geek-content-creator/creates";
        context.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.InvokeAsync(context);

        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Theory]
    // The signup routes are called by the portal's server with the API key, and the visitor's address and country it forwards
    // are believed only after that key is verified, so no onboarding route passes without one.
    [InlineData("/api/v1/onboarding/validate-eligibility")]
    [InlineData("/api/v1/onboarding/activate-subscription")]
    public async Task The_onboarding_routes_need_an_api_key(string path)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            new TestEnvironment("Production"));

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Theory]
    // Twilio's webhooks for a tenant's number: Twilio cannot send our API key, so TenantCallController checks X-Twilio-Signature
    // against the tenant's own subaccount token instead and refuses anything it does not match.
    [InlineData("/api/receptionist/tenant/22222222-2222-2222-2222-222222222222/voice", true)]
    [InlineData("/api/receptionist/tenant/22222222-2222-2222-2222-222222222222/call-status", true)]
    // That one prefix is the whole exemption. /api/receptionist/twilio was the company line's and was exempt until its routes were
    // deleted on 2026-11-20; it now needs credentials like any other path, which is what these rows pin.
    [InlineData("/api/receptionist/twilio/voice/incoming", false)]
    [InlineData("/api/receptionist/twilio/sms/incoming", false)]
    [InlineData("/api/receptionist/settings", false)]
    [InlineData("/api/receptionist/twilio", false)]
    [InlineData("/api/receptionist/tenant", false)]
    [InlineData("/api/receptionist/tenants/22222222-2222-2222-2222-222222222222/voice", false)]
    public async Task Only_the_tenant_call_webhooks_pass_without_an_api_key(string path, bool passes)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            new TestEnvironment("Production"));

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Equal(passes, reached);
        if (!passes) Assert.Equal(401, context.Response.StatusCode);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
