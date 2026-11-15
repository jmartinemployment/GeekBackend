using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Running a calendar tool for a tenant: the stored token is decrypted and used while it is fresh, renewed first (and the new one saved,
/// the refresh token left alone) when it has lapsed or lapses within two minutes, and handed to the gateway for one call. Every failure
/// is a named code, nothing is retried, a token the gateway still refuses is reauthorization_required, and no log line carries a token,
/// a key or an argument.
/// </summary>
public sealed class CalendarToolsTests
{
    private const string AccessToken = "ya29.STORED-ACCESS";
    private const string RefreshToken = "1//STORED-REFRESH";
    private const string RenewedToken = "ya29.RENEWED-ACCESS";
    private const string ClientSecret = "GOCSPX-client-secret-value";
    private const string GatewayKey = "gateway-key-value";
    private const string Secret = "Water heater repair for Dana";
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Key)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((request.RequestUri!.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("X-Internal-Api-Key", out var key) ? key.Single() : null));
            return respond(request);
        }
    }

    private sealed class Store : IVoiceStore
    {
        public Result<VoiceMcpConnection> Connection { get; set; } = Result<VoiceMcpConnection>.Failure("unset", VoiceFailureCodes.NotFound);
        public Func<Result<VoiceMcpConnection>>? Save { get; set; }
        public List<SaveVoiceMcpConnectionCommand> Saved { get; } = new();

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => Task.FromResult(Connection);

        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default)
        {
            Saved.Add(command);
            return Task.FromResult(Save is not null
                ? Save()
                : Result<VoiceMcpConnection>.Success(new VoiceMcpConnection(tenantId, provider, command.OauthAccessTokenEncrypted, command.OauthRefreshTokenEncrypted, command.TokenExpiresAt, command.ExternalCalendarId, true)));
        }

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage GatewayOk() =>
        Json("{\"tool\":\"list_calendars\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"structuredContent\":{\"calendars\":[{\"id\":\"primary\"}]}}");

    private static HttpResponseMessage RefreshOk() => Json($"{{\"access_token\":\"{RenewedToken}\",\"expires_in\":3599,\"scope\":\"x\",\"token_type\":\"Bearer\"}}");

    private sealed class Rig
    {
        public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public Store Store { get; } = new();
        public StubHandler Google { get; }
        public StubHandler Gateway { get; }
        public CapturingLogger<CalendarTools> ToolsLog { get; } = new();
        public CapturingLogger<GoogleCalendarOAuth> GoogleLog { get; } = new();
        public CapturingLogger<McpGatewayClient> GatewayLog { get; } = new();
        public VoiceCredentialProtector Protector { get; }
        public CalendarTools Tools { get; }
        public string AccessCipher { get; }
        public string RefreshCipher { get; }

        public Rig(
            DateTimeOffset? expiresAt = null,
            bool active = true,
            Func<HttpRequestMessage, HttpResponseMessage>? google = null,
            Func<HttpRequestMessage, HttpResponseMessage>? gateway = null,
            GoogleCalendarOptions? options = null,
            McpGatewayOptions? gatewayOptions = null)
        {
            Protector = new VoiceCredentialProtector(() => Key);
            AccessCipher = Protector.Protect(AccessToken).Value!;
            RefreshCipher = Protector.Protect(RefreshToken).Value!;
            Store.Connection = Result<VoiceMcpConnection>.Success(new VoiceMcpConnection(Tenant, GoogleCalendarOptions.Provider, AccessCipher, RefreshCipher, expiresAt ?? Now.AddMinutes(30), "primary", active));
            Google = new StubHandler(google ?? (_ => RefreshOk()));
            Gateway = new StubHandler(gateway ?? (_ => GatewayOk()));
            var clock = new Clock(Now);
            var googleOptions = options ?? new GoogleCalendarOptions("client-id.apps.googleusercontent.com", ClientSecret, "https://receptionist.geekatyourspot.com");
            var oauth = new GoogleCalendarOAuth(new HttpClient(Google), googleOptions, clock, GoogleLog);
            var gatewayClient = new McpGatewayClient(new HttpClient(Gateway), gatewayOptions ?? new McpGatewayOptions("http://geek-mcp-gateway.railway.internal:3000", GatewayKey), GatewayLog);
            Tools = new CalendarTools(Store, Protector, googleOptions, oauth, gatewayClient, clock, ToolsLog);
        }

        public IEnumerable<string> AllLogLines => ToolsLog.Lines.Concat(GoogleLog.Lines).Concat(GatewayLog.Lines);
    }

    private static JsonElement Args() => JsonDocument.Parse($"{{\"summary\":\"{Secret}\"}}").RootElement.Clone();

    [Fact]
    public async Task A_fresh_token_is_used_as_stored_with_no_renewal_and_one_gateway_call()
    {
        var rig = new Rig();
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.True(result.IsSuccess);
        Assert.Empty(rig.Google.Seen);
        var call = Assert.Single(rig.Gateway.Seen);
        Assert.Equal("http://geek-mcp-gateway.railway.internal:3000/v1/mcp/call", call.Url);
        Assert.Equal(GatewayKey, call.Key);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal(AccessToken, body.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal("list_calendars", body.RootElement.GetProperty("tool").GetString());
        Assert.Equal("google_calendar", body.RootElement.GetProperty("provider").GetString());
        Assert.Empty(rig.Store.Saved);
        Assert.True(result.Value!.StructuredContent!.Value.TryGetProperty("calendars", out _));
    }

    [Fact]
    public async Task The_arguments_reach_the_gateway_as_sent()
    {
        var rig = new Rig();
        await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "create_event", Args());

        using var body = JsonDocument.Parse(Assert.Single(rig.Gateway.Seen).Body);
        Assert.Equal(Secret, body.RootElement.GetProperty("arguments").GetProperty("summary").GetString());
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(119)]
    [InlineData(120)]
    public async Task A_token_lapsed_or_lapsing_within_two_minutes_is_renewed_saved_and_the_new_one_used(int secondsFromNow)
    {
        var rig = new Rig(expiresAt: Now.AddSeconds(secondsFromNow));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.True(result.IsSuccess);
        var refresh = Assert.Single(rig.Google.Seen);
        Assert.Equal("https://oauth2.googleapis.com/token", refresh.Url);
        Assert.Contains("grant_type=refresh_token", refresh.Body);
        Assert.Contains(Uri.EscapeDataString(RefreshToken), refresh.Body);
        Assert.Contains("client_id=client-id.apps.googleusercontent.com", refresh.Body);

        var saved = Assert.Single(rig.Store.Saved);
        Assert.Equal(RenewedToken, rig.Protector.Unprotect(saved.OauthAccessTokenEncrypted).Value);
        Assert.Equal(rig.RefreshCipher, saved.OauthRefreshTokenEncrypted);
        Assert.Equal(Now.AddSeconds(3599), saved.TokenExpiresAt);
        Assert.Equal("primary", saved.ExternalCalendarId);

        using var body = JsonDocument.Parse(Assert.Single(rig.Gateway.Seen).Body);
        Assert.Equal(RenewedToken, body.RootElement.GetProperty("accessToken").GetString());
    }

    [Fact]
    public async Task A_token_just_outside_the_window_is_not_renewed()
    {
        var rig = new Rig(expiresAt: Now.AddSeconds(121));
        await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);
        Assert.Empty(rig.Google.Seen);
    }

    [Fact]
    public async Task A_connection_with_no_known_expiry_is_renewed_first()
    {
        var rig = new Rig();
        rig.Store.Connection = Result<VoiceMcpConnection>.Success(rig.Store.Connection.Value! with { TokenExpiresAt = null });
        await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);
        Assert.Single(rig.Google.Seen);
    }

    [Fact]
    public async Task Google_refusing_the_refresh_token_is_reauthorization_required_and_nothing_else_happens()
    {
        var rig = new Rig(expiresAt: Now.AddMinutes(-5), google: _ => Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(McpFailureCodes.ReauthorizationRequired, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
        Assert.Empty(rig.Store.Saved);
        Assert.Single(rig.Google.Seen);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Any_other_refresh_failure_is_oauth_unavailable_and_the_gateway_is_not_called(HttpStatusCode status)
    {
        var rig = new Rig(expiresAt: Now.AddMinutes(-5), google: _ => Json("{}", status));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
        Assert.Single(rig.Google.Seen);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"x\"}")]
    [InlineData("{\"access_token\":\"x\",\"expires_in\":0}")]
    [InlineData("not json")]
    public async Task A_refresh_answer_missing_what_google_documents_is_oauth_unavailable(string answer)
    {
        var rig = new Rig(expiresAt: Now.AddMinutes(-5), google: _ => Json(answer));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task An_unreachable_google_on_refresh_is_oauth_unavailable()
    {
        var rig = new Rig(expiresAt: Now.AddMinutes(-5), google: _ => throw new HttpRequestException("down"));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);
        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task A_renewal_that_cannot_be_saved_fails_with_the_stores_code_and_the_gateway_is_not_called()
    {
        var rig = new Rig(expiresAt: Now.AddMinutes(-5));
        rig.Store.Save = () => Result<VoiceMcpConnection>.Failure("store down", VoiceFailureCodes.StoreUnavailable);
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
    }

    [Fact]
    public async Task An_unset_google_client_is_a_refusal_when_a_renewal_is_needed_but_not_when_it_is_not()
    {
        var unset = new GoogleCalendarOptions(null, null, null);
        var needed = new Rig(expiresAt: Now.AddMinutes(-5), options: unset);
        Assert.Equal(McpFailureCodes.OAuthNotConfigured, (await needed.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).ErrorCode);
        Assert.Empty(needed.Google.Seen);

        var notNeeded = new Rig(options: unset);
        Assert.True((await notNeeded.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).IsSuccess);
    }

    [Fact]
    public async Task A_token_the_gateway_still_refuses_is_reauthorization_required_and_is_not_renewed_or_repeated()
    {
        var rig = new Rig(gateway: _ => Json("{\"code\":\"token_rejected\"}", HttpStatusCode.Unauthorized));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(McpFailureCodes.ReauthorizationRequired, result.ErrorCode);
        Assert.Single(rig.Gateway.Seen);
        Assert.Empty(rig.Google.Seen);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "tool_error", McpFailureCodes.ToolError)]
    [InlineData(HttpStatusCode.BadRequest, "tool_not_allowed", McpFailureCodes.ToolNotAllowed)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request", VoiceFailureCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Forbidden, "provider_forbidden", McpFailureCodes.GatewayUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, "provider_unavailable", McpFailureCodes.GatewayUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error", McpFailureCodes.GatewayUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", McpFailureCodes.GatewayUnavailable)]
    public async Task Each_gateway_refusal_has_its_code_and_is_asked_once(HttpStatusCode status, string gatewayCode, string expected)
    {
        var rig = new Rig(gateway: _ => Json($"{{\"error\":\"x\",\"code\":\"{gatewayCode}\"}}", status));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(expected, result.ErrorCode);
        Assert.Single(rig.Gateway.Seen);
    }

    [Fact]
    public async Task A_gateway_that_is_unreachable_or_answers_nonsense_is_unavailable()
    {
        var down = new Rig(gateway: _ => throw new HttpRequestException("refused"));
        Assert.Equal(McpFailureCodes.GatewayUnavailable, (await down.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).ErrorCode);

        foreach (var nonsense in new[] { "not json", "{}", "{\"tool\":\"x\"}" })
        {
            var rig = new Rig(gateway: _ => Json(nonsense));
            var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);
            Assert.False(result.IsSuccess, nonsense);
            Assert.Equal(McpFailureCodes.GatewayUnavailable, result.ErrorCode);
        }
    }

    [Fact]
    public async Task An_unset_gateway_is_refused_before_anything_is_sent()
    {
        var rig = new Rig(gatewayOptions: new McpGatewayOptions(null, null));
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.Equal(McpFailureCodes.GatewayNotConfigured, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
    }

    [Fact]
    public async Task A_tenant_with_no_connection_or_an_inactive_one_is_calendar_not_connected_and_nothing_is_sent()
    {
        var none = new Rig();
        none.Store.Connection = Result<VoiceMcpConnection>.Failure("no such connection", VoiceFailureCodes.NotFound);
        Assert.Equal(McpFailureCodes.CalendarNotConnected, (await none.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).ErrorCode);
        Assert.Empty(none.Gateway.Seen);

        var inactive = new Rig(active: false);
        Assert.Equal(McpFailureCodes.CalendarNotConnected, (await inactive.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).ErrorCode);
        Assert.Empty(inactive.Gateway.Seen);
    }

    [Fact]
    public async Task A_store_failure_keeps_its_code()
    {
        var rig = new Rig();
        rig.Store.Connection = Result<VoiceMcpConnection>.Failure("store down", VoiceFailureCodes.StoreUnavailable);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, (await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null)).ErrorCode);
    }

    [Fact]
    public async Task Another_provider_is_refused_and_nothing_is_read()
    {
        var rig = new Rig();
        var result = await rig.Tools.CallAsync(Tenant, "outlook", "list_calendars", null);
        Assert.Equal(McpFailureCodes.ProviderNotSupported, result.ErrorCode);
        Assert.Empty(rig.Gateway.Seen);
    }

    [Fact]
    public async Task A_stored_token_that_cannot_be_decrypted_fails_and_nothing_is_sent()
    {
        var rig = new Rig();
        rig.Store.Connection = Result<VoiceMcpConnection>.Success(rig.Store.Connection.Value! with { OauthAccessTokenEncrypted = "not-ciphertext" });
        var result = await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "list_calendars", null);

        Assert.False(result.IsSuccess);
        Assert.Empty(rig.Gateway.Seen);
        Assert.Empty(rig.Google.Seen);
    }

    [Fact]
    public async Task No_log_line_carries_a_token_a_secret_the_gateway_key_or_an_argument()
    {
        var rigs = new[]
        {
            new Rig(),
            new Rig(expiresAt: Now.AddMinutes(-5)),
            new Rig(expiresAt: Now.AddMinutes(-5), google: _ => Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest)),
            new Rig(gateway: _ => Json("{\"code\":\"token_rejected\"}", HttpStatusCode.Unauthorized)),
            new Rig(gateway: _ => Json("{\"code\":\"tool_error\",\"content\":[{\"text\":\"" + Secret + "\"}]}", HttpStatusCode.UnprocessableEntity)),
            new Rig(gateway: _ => throw new HttpRequestException("down")),
        };
        foreach (var rig in rigs)
        {
            await rig.Tools.CallAsync(Tenant, GoogleCalendarOptions.Provider, "create_event", Args());
            foreach (var line in rig.AllLogLines)
            {
                Assert.DoesNotContain(AccessToken, line);
                Assert.DoesNotContain(RefreshToken, line);
                Assert.DoesNotContain(RenewedToken, line);
                Assert.DoesNotContain(ClientSecret, line);
                Assert.DoesNotContain(GatewayKey, line);
                Assert.DoesNotContain(Secret, line);
            }
        }
    }

    // ----- the owner's check route -----

    private static ClaimsPrincipal Owner(string? tenant) =>
        new(new ClaimsIdentity(tenant is null ? [] : [new Claim("tenant_id", tenant)], "test"));

    private static CalendarController Controller(Rig rig, ClaimsPrincipal user) =>
        new(rig.Tools) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };

    [Fact]
    public async Task The_check_route_answers_200_with_the_calendars_for_the_owners_own_tenant()
    {
        var rig = new Rig();
        var result = Assert.IsType<OkObjectResult>(await Controller(rig, Owner(Tenant.ToString())).Check(Tenant, default));

        Assert.NotNull(result.Value);
        Assert.Contains("\"connected\":true", JsonSerializer.Serialize(result.Value));
        Assert.Contains("primary", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(null, "tenant_unverified")]
    [InlineData("not-a-guid", "tenant_unverified")]
    [InlineData("00000000-0000-0000-0000-000000000000", "tenant_unverified")]
    [InlineData("99999999-2222-3333-4444-555555555555", "tenant_forbidden")]
    public async Task The_check_route_refuses_a_token_without_this_tenant_before_calling_anything(string? claim, string code)
    {
        var rig = new Rig();
        var result = Assert.IsAssignableFrom<ObjectResult>(await Controller(rig, Owner(claim)).Check(Tenant, default));

        Assert.Equal(403, result.StatusCode);
        Assert.Contains(code, JsonSerializer.Serialize(result.Value));
        Assert.Empty(rig.Gateway.Seen);
        Assert.Empty(rig.Google.Seen);
    }

    [Fact]
    public async Task The_check_route_answers_a_dead_connection_with_409_and_its_code()
    {
        var rig = new Rig(gateway: _ => Json("{\"code\":\"token_rejected\"}", HttpStatusCode.Unauthorized));
        var result = Assert.IsAssignableFrom<ObjectResult>(await Controller(rig, Owner(Tenant.ToString())).Check(Tenant, default));

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("reauthorization_required", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(McpFailureCodes.CalendarNotConnected, 404)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(McpFailureCodes.ReauthorizationRequired, 409)]
    [InlineData(McpFailureCodes.ProviderNotSupported, 400)]
    [InlineData(McpFailureCodes.ToolNotAllowed, 400)]
    [InlineData(McpFailureCodes.ToolError, 502)]
    [InlineData(McpFailureCodes.GatewayUnavailable, 503)]
    [InlineData(McpFailureCodes.GatewayNotConfigured, 503)]
    [InlineData(McpFailureCodes.OAuthUnavailable, 503)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData(null, 503)]
    public void Each_code_has_its_status(string? code, int status) => Assert.Equal(status, CalendarController.StatusFor(code));
}
