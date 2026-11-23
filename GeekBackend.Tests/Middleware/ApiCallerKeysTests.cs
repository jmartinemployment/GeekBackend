using GeekAPI.Auth;
using GeekAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.Middleware;

/// <summary>
/// Every server that calls GeekAPI with X-API-Key has a key of its own, GeekAPI says which caller authenticated, and a key left over
/// from before a caller was split off (the shared <c>GEEK_BACKEND_API_KEY</c>) still works only as <c>legacy</c>, loudly, until removed.
/// </summary>
[Collection(ApiKeyEnvironmentCollection.Name)]
public class ApiCallerKeysTests : IDisposable
{
    private static readonly string[] Managed =
    [
        "GEEK_API_CALLERS", "GEEK_BACKEND_API_KEY", "GEEK_API_KEY_PORTAL", "GEEK_API_KEY_WEBSITE", "GEEK_API_KEY_GEEK_REPORTS", "GEEK_API_KEY_ORPHAN",
    ];

    private readonly Dictionary<string, string?> _saved = Managed.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public ApiCallerKeysTests()
    {
        foreach (var name in Managed) Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved) Environment.SetEnvironmentVariable(name, value);
    }

    private static void Configure(string callers, params (string Variable, string Key)[] keys)
    {
        Environment.SetEnvironmentVariable("GEEK_API_CALLERS", callers);
        foreach (var (variable, key) in keys) Environment.SetEnvironmentVariable(variable, key);
    }

    [Fact]
    public void Each_callers_key_names_that_caller_and_no_other()
    {
        Configure("portal,website", ("GEEK_API_KEY_PORTAL", "portal-key"), ("GEEK_API_KEY_WEBSITE", "website-key"));

        Assert.Equal("portal", ApiCallerKeys.Authenticate("portal-key"));
        Assert.Equal("website", ApiCallerKeys.Authenticate("website-key"));
        Assert.Null(ApiCallerKeys.Authenticate("portal-key "));
        Assert.Null(ApiCallerKeys.Authenticate(""));
    }

    [Fact]
    public void A_hyphenated_name_reads_the_key_variable_with_underscores()
    {
        Assert.Equal("GEEK_API_KEY_GEEK_REPORTS", ApiCallerKeys.KeyVariableFor("geek-reports"));
        Configure("geek-reports", ("GEEK_API_KEY_GEEK_REPORTS", "reports-key"));

        Assert.Equal("geek-reports", ApiCallerKeys.Authenticate("reports-key"));
    }

    [Fact]
    public void The_shared_key_still_works_but_only_as_legacy()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"), ("GEEK_BACKEND_API_KEY", "shared-key"));

        Assert.Equal("legacy", ApiCallerKeys.Authenticate("shared-key"));
        Assert.Equal("portal", ApiCallerKeys.Authenticate("portal-key"));
    }

    [Fact]
    public void A_variable_no_listed_caller_reads_never_authenticates_anyone()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"), ("GEEK_API_KEY_ORPHAN", "orphan-key"));

        Assert.Null(ApiCallerKeys.Authenticate("orphan-key"));
        Assert.Contains(ApiCallerKeys.Problems(), p => p.Contains("GEEK_API_KEY_ORPHAN") && p.Contains("no caller"));
    }

    [Fact]
    public void A_valid_configuration_has_no_problems()
    {
        Configure("portal,website", ("GEEK_API_KEY_PORTAL", "portal-key"), ("GEEK_API_KEY_WEBSITE", "website-key"));

        Assert.Empty(ApiCallerKeys.Problems());
    }

    [Fact]
    public void No_key_at_all_is_a_problem_so_the_service_will_not_start()
    {
        Assert.Contains(ApiCallerKeys.Problems(), p => p.Contains("No API key is configured"));
    }

    [Fact]
    public void A_listed_caller_with_no_key_is_a_problem_and_names_the_variable()
    {
        Configure("portal,website", ("GEEK_API_KEY_PORTAL", "portal-key"));

        Assert.Contains(ApiCallerKeys.Problems(), p => p.Contains("GEEK_API_KEY_WEBSITE") && p.Contains("not set"));
    }

    [Theory]
    [InlineData("Portal Site")]
    [InlineData("portal_site")]
    [InlineData("-portal")]
    [InlineData("portal--site")]
    [InlineData("legacy")]
    public void A_malformed_or_reserved_name_is_a_problem(string name)
    {
        Configure(name, ("GEEK_API_KEY_PORTAL", "portal-key"));

        Assert.NotEmpty(ApiCallerKeys.Problems());
    }

    [Fact]
    public void Two_callers_holding_the_same_key_is_a_problem()
    {
        Configure("portal,website", ("GEEK_API_KEY_PORTAL", "same"), ("GEEK_API_KEY_WEBSITE", "same"));

        Assert.Contains(ApiCallerKeys.Problems(), p => p.Contains("portal, website") && p.Contains("same key"));
    }

    [Fact]
    public void A_caller_holding_the_shared_key_is_a_problem()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "shared-key"), ("GEEK_BACKEND_API_KEY", "shared-key"));

        Assert.Contains(ApiCallerKeys.Problems(), p => p.Contains("portal, legacy"));
    }

    [Fact]
    public void A_problem_names_variables_and_never_a_key()
    {
        Configure("portal,website", ("GEEK_API_KEY_PORTAL", "super-secret-value"), ("GEEK_API_KEY_WEBSITE", "super-secret-value"), ("GEEK_API_KEY_ORPHAN", "orphan-secret"));

        Assert.DoesNotContain(ApiCallerKeys.Problems(), p => p.Contains("super-secret-value") || p.Contains("orphan-secret"));
    }

    [Fact]
    public async Task The_middleware_records_which_caller_authenticated()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"));
        var (reached, context, _) = await Send("portal-key", "/api/crm/contacts");

        Assert.True(reached);
        Assert.Equal("portal", context.Items[ApiCallerKeys.CallerItem]);
        Assert.Equal(true, context.Items[CrmAuthConstants.VerifiedApiKeyItem]);
    }

    [Fact]
    public async Task The_middleware_refuses_a_key_no_caller_holds()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"));
        var (reached, context, _) = await Send("someone-elses-key", "/api/crm/contacts");

        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey(ApiCallerKeys.CallerItem));
        Assert.False(context.Items.ContainsKey(CrmAuthConstants.VerifiedApiKeyItem));
    }

    [Fact]
    public async Task The_middleware_warns_on_every_use_of_the_shared_key_and_never_logs_a_key()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"), ("GEEK_BACKEND_API_KEY", "shared-key"));
        var (reached, context, log) = await Send("shared-key");

        Assert.True(reached);
        Assert.Equal("legacy", context.Items[ApiCallerKeys.CallerItem]);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("legacy", warning.Message);
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("shared-key") || e.Message.Contains("portal-key"));
    }

    [Fact]
    public async Task The_internal_api_paths_take_the_same_caller_keys()
    {
        Configure("portal", ("GEEK_API_KEY_PORTAL", "portal-key"));
        var (reached, _, _) = await Send("portal-key", "/api/seo/internal/anything");
        var (refusedReached, refused, _) = await Send("wrong", "/api/seo/internal/anything");

        Assert.True(reached);
        Assert.False(refusedReached);
        Assert.Equal(401, refused.Response.StatusCode);
    }

    private static async Task<(bool Reached, DefaultHttpContext Context, RecordingLogger Log)> Send(string key, string path = "/api/visitors/internal/events")
    {
        var reached = false;
        var log = new RecordingLogger();
        var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new TestEnvironment("Production"), log);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.Headers["X-API-Key"] = key;
        await middleware.InvokeAsync(context);
        return (reached, context, log);
    }

    private sealed class RecordingLogger : ILogger<ApiKeyMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>
/// One collection for every test that sets the process-wide API key variables, so xunit does not run two of them at once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiKeyEnvironmentCollection
{
    public const string Name = "ApiKeyEnvironment";
}
