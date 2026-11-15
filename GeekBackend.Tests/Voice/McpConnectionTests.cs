using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GeekAPI.Controllers.Voice;
using GeekAPI.Middleware;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Connecting a tenant's Google Calendar: the state is sealed and tied to one tenant, one provider and ten minutes; Google's one-time code
/// is spent only after the state checks out; an approval started for one tenant cannot be finished by another; only ciphertext reaches the
/// store; every Google failure is a named code and never a default; and no route is open without a token. No log line carries a code, a
/// token or a secret.
/// </summary>
public sealed class McpConnectionTests
{
    private const string AccessToken = "ya29.ACCESS-TOKEN-VALUE";
    private const string RefreshToken = "1//REFRESH-TOKEN-VALUE";
    private const string AuthCode = "4/0AUTH-CODE-VALUE";
    private const string ClientSecret = "GOCSPX-client-secret-value";
    private const string Portal = "https://receptionist.geekatyourspot.com";
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
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
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private sealed class Store : IVoiceStore
    {
        public Result<VoiceTenant> TenantResult { get; set; } = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.Active));
        public Func<Result<VoiceMcpConnection>>? Save { get; set; }
        public List<(Guid Tenant, string Provider, SaveVoiceMcpConnectionCommand Command)> Saved { get; } = new();

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(TenantResult);

        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default)
        {
            Saved.Add((tenantId, provider, command));
            return Task.FromResult(Save is not null
                ? Save()
                : Result<VoiceMcpConnection>.Success(new VoiceMcpConnection(tenantId, provider, command.OauthAccessTokenEncrypted, command.OauthRefreshTokenEncrypted, command.TokenExpiresAt, command.ExternalCalendarId, true)));
        }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public Clock Clock { get; } = new(new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero));
        public Store Store { get; } = new();
        public StubHandler Google { get; }
        public CapturingLogger<GoogleCalendarOAuth> GoogleLog { get; } = new();
        public CapturingLogger<McpConnectionService> ServiceLog { get; } = new();
        public GoogleCalendarOptions Options { get; }
        public VoiceCredentialProtector Protector { get; }
        public McpOAuthState State { get; }
        public McpConnectionService Service { get; }

        public Rig(Func<HttpRequestMessage, HttpResponseMessage>? google = null, GoogleCalendarOptions? options = null)
        {
            Google = new StubHandler(google ?? (_ => TokenAnswer()));
            Options = options ?? new GoogleCalendarOptions("client-id.apps.googleusercontent.com", ClientSecret, Portal);
            Protector = new VoiceCredentialProtector(() => Key);
            State = new McpOAuthState(Protector, Clock);
            var oauth = new GoogleCalendarOAuth(new HttpClient(Google), Options, Clock, GoogleLog);
            Service = new McpConnectionService(Store, Options, oauth, State, Protector, ServiceLog);
        }

        public string SealedState(Guid? tenant = null, string provider = GoogleCalendarOptions.Provider) =>
            State.Create(tenant ?? Tenant, provider).Value!;

        public McpOAuthController Controller(ClaimsPrincipal? user = null) => new(Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) } },
        };
    }

    private static VoiceTenant MakeTenant(string status) => new(
        Tenant, "Dag Plumbing", "plumbing", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", null, "owner@example.com", "+15615550100", status, DateTimeOffset.UnixEpoch);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TokenAnswer(string? refresh = RefreshToken, string? scope = null, string? access = AccessToken, int expiresIn = 3599)
    {
        scope ??= string.Join(' ', GoogleCalendarOptions.Scopes);
        var parts = new List<string> { $"\"expires_in\":{expiresIn}", $"\"scope\":\"{scope}\"", "\"token_type\":\"Bearer\"" };
        if (access is not null) parts.Add($"\"access_token\":\"{access}\"");
        if (refresh is not null) parts.Add($"\"refresh_token\":\"{refresh}\"");
        return Json("{" + string.Join(',', parts) + "}");
    }

    private static ClaimsPrincipal Owner(Guid? tenant) =>
        new(new ClaimsIdentity(tenant is null ? [] : [new Claim("tenant_id", tenant.Value.ToString())], "test"));

    // ---- state ----

    [Fact]
    public void A_state_reads_back_as_the_tenant_it_was_made_for()
    {
        var rig = new Rig();

        var read = rig.State.Read(rig.SealedState(), GoogleCalendarOptions.Provider);

        Assert.True(read.IsSuccess, read.Error);
        Assert.Equal(Tenant, read.Value);
    }

    [Fact]
    public void A_state_made_for_one_provider_is_refused_for_another()
    {
        var rig = new Rig();

        var read = rig.State.Read(rig.SealedState(provider: "hubspot"), GoogleCalendarOptions.Provider);

        Assert.False(read.IsSuccess);
        Assert.Equal(McpFailureCodes.StateInvalid, read.ErrorCode);
    }

    [Fact]
    public void A_state_older_than_ten_minutes_is_expired()
    {
        var rig = new Rig();
        var sealedState = rig.SealedState();
        rig.Clock.Now = rig.Clock.Now.AddMinutes(10).AddSeconds(1);

        var read = rig.State.Read(sealedState, GoogleCalendarOptions.Provider);

        Assert.False(read.IsSuccess);
        Assert.Equal(McpFailureCodes.StateExpired, read.ErrorCode);
    }

    [Fact]
    public void A_state_at_exactly_ten_minutes_still_reads()
    {
        var rig = new Rig();
        var sealedState = rig.SealedState();
        rig.Clock.Now = rig.Clock.Now.AddMinutes(10);

        Assert.True(rig.State.Read(sealedState, GoogleCalendarOptions.Provider).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-state")]
    public void A_blank_or_unsealed_state_is_invalid(string? value)
    {
        var rig = new Rig();

        var read = rig.State.Read(value, GoogleCalendarOptions.Provider);

        Assert.False(read.IsSuccess);
        Assert.Equal(McpFailureCodes.StateInvalid, read.ErrorCode);
    }

    [Fact]
    public void A_state_sealed_under_another_key_is_invalid()
    {
        var rig = new Rig();
        var other = new Rig();

        var read = rig.State.Read(other.SealedState(), GoogleCalendarOptions.Provider);

        Assert.False(read.IsSuccess);
        Assert.Equal(McpFailureCodes.StateInvalid, read.ErrorCode);
    }

    [Fact]
    public void An_unusable_key_is_encryption_unavailable_and_never_a_bad_state()
    {
        var state = new McpOAuthState(new VoiceCredentialProtector(() => null), new Clock(DateTimeOffset.UnixEpoch));

        var made = state.Create(Tenant, GoogleCalendarOptions.Provider);
        var read = state.Read(new Rig().SealedState(), GoogleCalendarOptions.Provider);

        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, made.ErrorCode);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, read.ErrorCode);
    }

    // ---- begin ----

    [Fact]
    public async Task Begin_returns_the_Google_address_with_the_registered_redirect_the_three_scopes_and_a_state_for_the_tenant()
    {
        var rig = new Rig();

        var result = await rig.Service.BeginAsync(Tenant, GoogleCalendarOptions.Provider);

        Assert.True(result.IsSuccess, result.Error);
        var url = new Uri(result.Value!);
        Assert.Equal("accounts.google.com", url.Host);
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("client-id.apps.googleusercontent.com", query["client_id"]);
        Assert.Equal("https://receptionist.geekatyourspot.com/oauth/mcp/google_calendar/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal(string.Join(' ', GoogleCalendarOptions.Scopes), query["scope"]);
        Assert.Equal(3, query["scope"]!.Split(' ').Length);
        Assert.Equal(Tenant, rig.State.Read(query["state"], GoogleCalendarOptions.Provider).Value);
        Assert.DoesNotContain(ClientSecret, result.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Begin_refuses_a_provider_that_is_not_built()
    {
        var rig = new Rig();

        var result = await rig.Service.BeginAsync(Tenant, "hubspot");

        Assert.Equal(McpFailureCodes.ProviderNotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task Begin_refuses_a_tenant_that_has_not_paid()
    {
        var rig = new Rig();
        rig.Store.TenantResult = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.PendingPayment));

        var result = await rig.Service.BeginAsync(Tenant, GoogleCalendarOptions.Provider);

        Assert.Equal(McpFailureCodes.TenantNotActive, result.ErrorCode);
    }

    [Fact]
    public async Task Begin_passes_on_a_missing_tenant_as_not_found()
    {
        var rig = new Rig();
        rig.Store.TenantResult = Result<VoiceTenant>.Failure("No such tenant.", VoiceFailureCodes.NotFound);

        var result = await rig.Service.BeginAsync(Tenant, GoogleCalendarOptions.Provider);

        Assert.Equal(VoiceFailureCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task An_unset_Google_variable_names_itself_in_the_log_and_is_oauth_not_configured()
    {
        var rig = new Rig(options: new GoogleCalendarOptions(null, ClientSecret, Portal));

        var result = await rig.Service.BeginAsync(Tenant, GoogleCalendarOptions.Provider);

        Assert.Equal(McpFailureCodes.OAuthNotConfigured, result.ErrorCode);
        Assert.Contains(rig.ServiceLog.Lines, line => line.Contains("GOOGLE_OAUTH_CLIENT_ID", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.ServiceLog.Lines, line => line.Contains("GOOGLE_OAUTH_CLIENT_SECRET", StringComparison.Ordinal));
    }

    // ---- complete ----

    [Fact]
    public async Task Complete_saves_both_tokens_encrypted_with_the_expiry_and_the_primary_calendar()
    {
        var rig = new Rig();

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Tenant, result.Value);
        var saved = Assert.Single(rig.Store.Saved);
        Assert.Equal((Tenant, GoogleCalendarOptions.Provider), (saved.Tenant, saved.Provider));
        Assert.DoesNotContain(AccessToken, saved.Command.OauthAccessTokenEncrypted, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, saved.Command.OauthRefreshTokenEncrypted, StringComparison.Ordinal);
        Assert.Equal(AccessToken, rig.Protector.Unprotect(saved.Command.OauthAccessTokenEncrypted).Value);
        Assert.Equal(RefreshToken, rig.Protector.Unprotect(saved.Command.OauthRefreshTokenEncrypted).Value);
        Assert.Equal(rig.Clock.Now.AddSeconds(3599), saved.Command.TokenExpiresAt);
        Assert.Equal("primary", saved.Command.ExternalCalendarId);
    }

    [Fact]
    public async Task Complete_sends_Google_the_code_the_client_and_the_exact_registered_redirect_once()
    {
        var rig = new Rig();

        await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        var body = Assert.Single(rig.Google.Bodies);
        var form = System.Web.HttpUtility.ParseQueryString(body);
        Assert.Equal(AuthCode, form["code"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("https://receptionist.geekatyourspot.com/oauth/mcp/google_calendar/callback", form["redirect_uri"]);
        Assert.Equal(ClientSecret, form["client_secret"]);
    }

    [Fact]
    public async Task A_forged_state_is_refused_before_Google_is_asked()
    {
        var rig = new Rig();

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, "forged", null);

        Assert.Equal(McpFailureCodes.StateInvalid, result.ErrorCode);
        Assert.Empty(rig.Google.Bodies);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task A_declined_approval_is_authorization_denied_and_spends_nothing()
    {
        var rig = new Rig();

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, null, rig.SealedState(), "access_denied");

        Assert.Equal(McpFailureCodes.AuthorizationDenied, result.ErrorCode);
        Assert.Empty(rig.Google.Bodies);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task A_callback_with_no_code_is_an_invalid_request()
    {
        var rig = new Rig();

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, " ", rig.SealedState(), null);

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(rig.Google.Bodies);
    }

    [Fact]
    public async Task Complete_refuses_a_provider_that_is_not_built_without_reading_the_state()
    {
        var rig = new Rig();

        var result = await rig.Service.CompleteAsync(Tenant, "outlook", AuthCode, rig.SealedState(provider: "outlook"), null);

        Assert.Equal(McpFailureCodes.ProviderNotSupported, result.ErrorCode);
        Assert.Empty(rig.Google.Bodies);
    }

    [Fact]
    public async Task Google_saying_invalid_grant_is_the_visitors_mistake()
    {
        var rig = new Rig(_ => Json("{\"error\":\"invalid_grant\",\"error_description\":\"Bad Request\"}", HttpStatusCode.BadRequest));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.CodeExchangeFailed, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Google_refusing_our_client_or_failing_is_oauth_unavailable(HttpStatusCode status)
    {
        var rig = new Rig(_ => Json("{\"error\":\"invalid_client\"}", status));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
        Assert.Single(rig.Google.Bodies);
    }

    [Fact]
    public async Task An_unreachable_Google_is_oauth_unavailable_and_is_not_retried()
    {
        var rig = new Rig(_ => throw new HttpRequestException("connection reset"));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
        Assert.Single(rig.Google.Bodies);
    }

    [Fact]
    public async Task An_answer_that_is_not_JSON_is_oauth_unavailable()
    {
        var rig = new Rig(_ => Json("<html>oops</html>"));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task An_answer_missing_the_access_token_is_oauth_unavailable()
    {
        var rig = new Rig(_ => TokenAnswer(access: null));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.OAuthUnavailable, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task An_answer_with_no_refresh_token_is_refused_and_nothing_is_saved()
    {
        var rig = new Rig(_ => TokenAnswer(refresh: null));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.RefreshTokenMissing, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task A_person_who_unticks_the_event_scope_is_refused_because_booking_would_fail_later()
    {
        var rig = new Rig(_ => TokenAnswer(scope: "https://www.googleapis.com/auth/calendar.calendarlist.readonly"));

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(McpFailureCodes.ScopeNotGranted, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task A_store_failure_is_passed_on_and_the_connection_is_not_reported_done()
    {
        var rig = new Rig();
        rig.Store.Save = () => Result<VoiceMcpConnection>.Failure("The store is down.", VoiceFailureCodes.StoreUnavailable);

        var result = await rig.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task An_unusable_encryption_key_stops_the_save_before_anything_unencrypted_is_stored()
    {
        var rig = new Rig();
        var oauth = new GoogleCalendarOAuth(new HttpClient(rig.Google), rig.Options, rig.Clock, rig.GoogleLog);
        var noKey = new VoiceCredentialProtector(() => null);
        var service = new McpConnectionService(rig.Store, rig.Options, oauth, new McpOAuthState(rig.Protector, rig.Clock), noKey, rig.ServiceLog);

        var result = await service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, rig.SealedState(), null);

        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, result.ErrorCode);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task No_log_line_carries_a_code_a_token_or_the_secret()
    {
        var failing = new Rig(_ => Json("{\"error\":\"invalid_client\"}", HttpStatusCode.Unauthorized));
        await failing.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, failing.SealedState(), null);
        var working = new Rig();
        await working.Service.CompleteAsync(Tenant, GoogleCalendarOptions.Provider, AuthCode, working.SealedState(), null);

        var lines = failing.GoogleLog.Lines.Concat(failing.ServiceLog.Lines).Concat(working.GoogleLog.Lines).Concat(working.ServiceLog.Lines).ToList();
        Assert.NotEmpty(lines);
        foreach (var secret in new[] { AuthCode, AccessToken, RefreshToken, ClientSecret })
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.Ordinal));
    }

    // ---- controller ----

    [Fact]
    public async Task Authorize_returns_the_address_for_the_tenant_in_the_token()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Tenant)).Authorize(Tenant, GoogleCalendarOptions.Provider, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(answer);
        Assert.Contains("accounts.google.com", ok.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_refuses_a_token_with_no_tenant()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(null)).Authorize(Tenant, GoogleCalendarOptions.Provider, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(answer).StatusCode);
        Assert.Contains(McpFailureCodes.TenantUnverified, Assert.IsType<ObjectResult>(answer).Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_refuses_a_tenant_in_the_URL_that_is_not_the_one_in_the_token()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Guid.NewGuid())).Authorize(Tenant, GoogleCalendarOptions.Provider, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(answer).StatusCode);
        Assert.Contains(McpFailureCodes.TenantForbidden, Assert.IsType<ObjectResult>(answer).Value!.ToString(), StringComparison.Ordinal);
        Assert.Empty(rig.Google.Bodies);
    }

    [Fact]
    public async Task Authorize_answers_an_unpaid_tenant_with_409()
    {
        var rig = new Rig();
        rig.Store.TenantResult = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.PendingPayment));

        var answer = await rig.Controller(Owner(Tenant)).Authorize(Tenant, GoogleCalendarOptions.Provider, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(answer).StatusCode);
    }

    private static CompleteMcpConnectionRequest Approval(string? state, string? code = AuthCode, string? error = null) => new(code, state, error);

    [Fact]
    public async Task Complete_connects_the_calendar_for_the_owner_whose_token_matches_the_state()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Tenant)).Complete(Tenant, GoogleCalendarOptions.Provider, Approval(rig.SealedState()), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(answer);
        Assert.Contains("connected = True", ok.Value!.ToString(), StringComparison.Ordinal);
        Assert.Single(rig.Store.Saved);
    }

    [Fact]
    public async Task Complete_refuses_an_approval_started_for_another_tenant_and_never_asks_Google()
    {
        var rig = new Rig();
        var stranger = Guid.NewGuid();

        var answer = await rig.Controller(Owner(stranger)).Complete(stranger, GoogleCalendarOptions.Provider, Approval(rig.SealedState(Tenant)), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains(McpFailureCodes.TenantForbidden, refused.Value!.ToString(), StringComparison.Ordinal);
        Assert.Empty(rig.Google.Bodies);
        Assert.Empty(rig.Store.Saved);
        Assert.Contains(rig.ServiceLog.Lines, line => line.Contains(Tenant.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Complete_refuses_a_token_with_no_tenant_and_a_tenant_in_the_URL_that_is_not_the_tokens()
    {
        var rig = new Rig();

        var none = await rig.Controller(Owner(null)).Complete(Tenant, GoogleCalendarOptions.Provider, Approval(rig.SealedState()), CancellationToken.None);
        var other = await rig.Controller(Owner(Guid.NewGuid())).Complete(Tenant, GoogleCalendarOptions.Provider, Approval(rig.SealedState()), CancellationToken.None);

        Assert.Contains(McpFailureCodes.TenantUnverified, Assert.IsType<ObjectResult>(none).Value!.ToString(), StringComparison.Ordinal);
        Assert.Contains(McpFailureCodes.TenantForbidden, Assert.IsType<ObjectResult>(other).Value!.ToString(), StringComparison.Ordinal);
        Assert.Empty(rig.Google.Bodies);
    }

    [Fact]
    public async Task Complete_with_a_forged_state_is_a_400_and_stores_nothing()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Tenant)).Complete(Tenant, GoogleCalendarOptions.Provider, Approval("forged"), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains(McpFailureCodes.StateInvalid, refused.Value!.ToString(), StringComparison.Ordinal);
        Assert.Empty(rig.Store.Saved);
        Assert.Empty(rig.Google.Bodies);
    }

    [Fact]
    public async Task Complete_with_a_declined_approval_is_a_400_with_the_failure_code()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Tenant)).Complete(Tenant, GoogleCalendarOptions.Provider, Approval(rig.SealedState(), code: null, error: "access_denied"), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains(McpFailureCodes.AuthorizationDenied, refused.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_with_no_body_is_a_400()
    {
        var rig = new Rig();

        var answer = await rig.Controller(Owner(Tenant)).Complete(Tenant, GoogleCalendarOptions.Provider, null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(answer);
    }

    [Fact]
    public async Task Complete_with_no_portal_address_is_503_oauth_not_configured_before_Google_is_asked()
    {
        var rig = new Rig(options: new GoogleCalendarOptions("id", ClientSecret, null));

        var answer = await rig.Controller(Owner(Tenant)).Complete(Tenant, GoogleCalendarOptions.Provider, Approval(rig.SealedState()), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
        Assert.Contains(McpFailureCodes.OAuthNotConfigured, refused.Value!.ToString(), StringComparison.Ordinal);
        Assert.Contains(rig.ServiceLog.Lines, line => line.Contains("PORTAL_BASE_URL", StringComparison.Ordinal));
        Assert.Empty(rig.Google.Bodies);
    }

    [Theory]
    [InlineData(McpFailureCodes.ProviderNotSupported, 400)]
    [InlineData(McpFailureCodes.StateInvalid, 400)]
    [InlineData(McpFailureCodes.CodeExchangeFailed, 400)]
    [InlineData(McpFailureCodes.TenantUnverified, 403)]
    [InlineData(McpFailureCodes.TenantForbidden, 403)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(McpFailureCodes.TenantNotActive, 409)]
    [InlineData(McpFailureCodes.OAuthNotConfigured, 503)]
    [InlineData(McpFailureCodes.OAuthUnavailable, 503)]
    [InlineData(VoiceCredentialProtector.EncryptionUnavailable, 503)]
    [InlineData(null, 503)]
    public void Each_failure_code_has_its_own_status(string? code, int status)
    {
        Assert.Equal(status, McpOAuthController.StatusFor(code));
    }

    // ---- no route here is open: Google never calls GeekAPI, the portal does, with the owner's token ----

    private sealed class Environment_ : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "GeekAPI";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(bool Reached, int Status)> Through(string path)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new Environment_());
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return (reached, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/oauth/mcp/google_calendar/callback")]
    [InlineData("/api/v1/tenants/11111111-2222-3333-4444-555555555555/oauth/mcp/google_calendar/authorize")]
    [InlineData("/api/v1/tenants/11111111-2222-3333-4444-555555555555/oauth/mcp/google_calendar/complete")]
    public async Task No_calendar_connection_route_passes_without_a_key_or_a_token(string path)
    {
        var (reached, status) = await Through(path);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }
}
