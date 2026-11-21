using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.Voice;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// /internal/v1/* over HTTP: each status GeekRepository can answer becomes the failure code the callers branch on, a store that is
/// down is never an empty result, each call is made once, and no log line carries a body.
/// </summary>
public sealed class HttpVoiceRepositoryTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return respond(request);
        }
    }

    private sealed class CapturingLogger : ILogger<HttpVoiceRepository>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private static (HttpVoiceRepository Repo, StubHandler Handler, CapturingLogger Log) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var log = new CapturingLogger();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://geek-repository.test/") };
        return (new HttpVoiceRepository(client, log), handler, log);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, WebJson), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static object TenantBody() => new
    {
        id = Tenant, businessName = "Boca Cool HVAC", industryVertical = "Plumbing, heating, & air-conditioning contractors", complianceAttestedAt = DateTimeOffset.UnixEpoch,
        countryCode = "US", stateCode = "FL", contactEmail = "owner@bocacool.example", contactPhone = "+15615550101", status = "pending_payment", createdAt = DateTimeOffset.UnixEpoch,
    };

    private static CreateVoiceTenantCommand SomeTenant() => new("Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", DateTimeOffset.UnixEpoch, "US", "FL", "owner@bocacool.example", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(DateTimeOffset.UnixEpoch), Attestation = AttestationFixture.Good(DateTimeOffset.UnixEpoch) };

    [Fact]
    public async Task Creating_a_tenant_posts_the_command_and_reads_the_tenant()
    {
        var (repo, handler, _) = Build(_ => Json(TenantBody()));

        var result = await repo.CreateTenantAsync(SomeTenant());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Tenant, result.Value!.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/internal/v1/onboarding/tenants", request.Path);
        Assert.Contains("\"businessName\":\"Boca Cool HVAC\"", request.Body);
    }

    [Fact]
    public async Task Each_call_goes_to_its_route_in_the_plan()
    {
        var (repo, handler, _) = Build(_ => Text(HttpStatusCode.NotFound, "no such thing"));

        await repo.GetTenantAsync(Tenant);
        await repo.GetTelephonyAsync(Tenant);
        await repo.GetConfigAsync(Tenant);
        await repo.GetMcpConnectionAsync(Tenant, VoiceMcpProviders.GoogleCalendar);
        await repo.SaveMcpConnectionAsync(Tenant, VoiceMcpProviders.GoogleCalendar, new SaveVoiceMcpConnectionCommand("a", "r", null, null));

        Assert.Equal(
            [
                (HttpMethod.Get, $"/internal/v1/onboarding/tenants/{Tenant}"),
                (HttpMethod.Get, $"/internal/v1/tenants/{Tenant}/telephony"),
                (HttpMethod.Get, $"/internal/v1/tenants/{Tenant}/config"),
                (HttpMethod.Get, $"/internal/v1/tenants/{Tenant}/mcp/google_calendar"),
                (HttpMethod.Put, $"/internal/v1/tenants/{Tenant}/mcp/google_calendar"),
            ],
            handler.Requests.Select(r => (r.Method, r.Path)));
    }

    /// <summary>
    /// Withdrawing a connection is a DELETE on that connection's own route, sent with no body, and the store's answer is read as it
    /// comes: "there was nothing to remove" is a success that says so, never a failure and never a claim that something was removed.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_is_a_delete_on_the_connections_route_and_reports_what_the_store_answered()
    {
        var (repo, handler, _) = Build(request => Json(new { removed = request.RequestUri!.AbsolutePath.EndsWith("/storage", StringComparison.Ordinal) }));

        var mcp = await repo.DisconnectMcpAsync(Tenant, VoiceMcpProviders.GoogleCalendar);
        var storage = await repo.DisconnectStorageAsync(Tenant);

        Assert.True(mcp.IsSuccess, mcp.Error);
        Assert.False(mcp.Value!.Removed);
        Assert.True(storage.IsSuccess, storage.Error);
        Assert.True(storage.Value!.Removed);
        Assert.Equal(
            [
                (HttpMethod.Delete, $"/internal/v1/tenants/{Tenant}/mcp/google_calendar"),
                (HttpMethod.Delete, $"/internal/v1/tenants/{Tenant}/storage"),
            ],
            handler.Requests.Select(r => (r.Method, r.Path)));
        Assert.All(handler.Requests, r => Assert.Null(r.Body));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "no such tenant", VoiceFailureCodes.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, "A business name is required.", VoiceFailureCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Conflict, "already active", VoiceFailureCodes.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "database down", VoiceFailureCodes.StoreUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, "", VoiceFailureCodes.StoreUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "boom", VoiceFailureCodes.StoreUnavailable)]
    public async Task A_status_becomes_its_failure_code(HttpStatusCode status, string body, string code)
    {
        var (repo, handler, _) = Build(_ => Text(status, body));

        var result = await repo.GetTenantAsync(Tenant);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_404_with_no_message_is_a_missing_route_and_never_a_missing_tenant()
    {
        var (repo, _, log) = Build(_ => Text(HttpStatusCode.NotFound, ""));

        var result = await repo.GetTenantAsync(Tenant);

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Contains(log.Lines, l => l.Contains("404", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_is_unavailable_and_is_logged()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var log = new CapturingLogger();
        var repo = new HttpVoiceRepository(new HttpClient(handler) { BaseAddress = new Uri("http://geek-repository.test/") }, log);

        var result = await repo.GetTelephonyAsync(Tenant);

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.NotEmpty(log.Lines);
    }


    [Fact]
    public async Task A_body_that_does_not_parse_is_unavailable()
    {
        var (repo, _, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>", Encoding.UTF8, "application/json") });

        var result = await repo.GetTenantAsync(Tenant);

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task No_log_line_carries_a_request_or_response_body()
    {
        var (repo, _, log) = Build(_ => Text(HttpStatusCode.InternalServerError, "Boca Cool HVAC owner@bocacool.example +15615550101"));

        await repo.CreateTenantAsync(SomeTenant());

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, line =>
        {
            Assert.DoesNotContain("Boca Cool", line, StringComparison.Ordinal);
            Assert.DoesNotContain("owner@bocacool.example", line, StringComparison.Ordinal);
            Assert.DoesNotContain("+15615550101", line, StringComparison.Ordinal);
        });
    }
}
