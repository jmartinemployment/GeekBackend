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

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
