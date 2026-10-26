using GeekAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.Tests.Visitors;

/// <summary>Every visitor route sits under /api/visitors/internal/, which ApiKeyMiddleware guards.</summary>
public sealed class VisitorRoutesRequireApiKeyTests
{
    [Theory]
    [InlineData("POST", "/api/visitors/internal/events")]
    [InlineData("POST", "/api/visitors/internal/identify")]
    [InlineData("POST", "/api/visitors/internal/drops")]
    [InlineData("GET", "/api/visitors/internal/reports/2026-10-17")]
    [InlineData("GET", "/api/visitors/internal/reports/2026-10-17/summary")]
    [InlineData("GET", "/api/visitors/internal/reports/2026-10-17/sessions.csv")]
    public async Task Without_a_key_the_route_answers_401_and_the_pipeline_stops(string method, string path)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new TestEnvironment("Production"));
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task With_the_key_the_route_is_reached()
    {
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "visitor-test-key");
        try
        {
            var reached = false;
            var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new TestEnvironment("Production"));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/visitors/internal/events";
            context.Request.Headers["X-API-Key"] = "visitor-test-key";

            await middleware.InvokeAsync(context);

            Assert.True(reached);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", null);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
