using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using GeekAPI.Controllers.Voice;
using GeekAPI.HttpClients;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Geek-Receptionist's own owner login: only an active tenant gets one, the password is stored only as a hash, signing in refuses an
/// unknown email and a wrong password alike, the tokens are signed with Geek-Receptionist's own key and refused when signed with any other,
/// a refresh token is spent once and replaced, one presented again ends the sign-in, an unset key refuses sign-in before anything is
/// read, every owner route names Geek-Receptionist's scheme and not GeekOAuth's, and no password or token is ever logged.
/// </summary>
public sealed class OwnerAuthTests
{
    private const string Password = "correct-horse-battery-staple";
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private sealed class Store(Result<VoiceTenant> tenant) : IVoiceStore
    {
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(tenant);
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>The owner store as GeekRepository behaves: one login per tenant and per email, a refresh token spent once, a replay ending the family.</summary>
    private sealed class Owners : IVoiceOwnerStore
    {
        public List<VoiceOwnerLogin> Logins { get; } = new();
        public List<(StoreVoiceRefreshTokenCommand Token, bool Used, bool Revoked)> Tokens { get; } = new();
        public int Calls { get; private set; }

        public Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default)
        {
            Calls++;
            var email = command.Email.ToLowerInvariant();
            if (Logins.Any(l => l.TenantId == command.TenantId || l.Email == email))
                return Task.FromResult(Result<VoiceOwnerLogin>.Failure("exists", VoiceFailureCodes.Conflict));
            var login = new VoiceOwnerLogin(Guid.NewGuid(), command.TenantId, email, command.PasswordHash, command.DisplayName);
            Logins.Add(login);
            return Task.FromResult(Result<VoiceOwnerLogin>.Success(login));
        }

        public Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default)
        {
            Calls++;
            var login = Logins.FirstOrDefault(l => l.Email == email.Trim().ToLowerInvariant());
            return Task.FromResult(login is null ? Result<VoiceOwnerLogin>.Failure("No login.", VoiceFailureCodes.NotFound) : Result<VoiceOwnerLogin>.Success(login));
        }

        public Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default)
        {
            Calls++;
            Tokens.Add((command, false, false));
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<ConsumedVoiceRefreshToken>> ConsumeRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
        {
            Calls++;
            var i = Tokens.FindIndex(t => t.Token.TokenHash == tokenHash);
            if (i < 0) return Task.FromResult(Result<ConsumedVoiceRefreshToken>.Failure("Unknown.", VoiceFailureCodes.NotFound));
            var (token, used, revoked) = Tokens[i];
            if (used || revoked)
            {
                for (var j = 0; j < Tokens.Count; j++)
                    if (Tokens[j].Token.FamilyId == token.FamilyId) Tokens[j] = (Tokens[j].Token, Tokens[j].Used, true);
                return Task.FromResult(Result<ConsumedVoiceRefreshToken>.Failure("Reused.", VoiceOwnerFailureCodes.RefreshTokenReused));
            }
            Tokens[i] = (token, true, false);
            var login = Logins.Single(l => l.Id == token.OwnerLoginId);
            return Task.FromResult(Result<ConsumedVoiceRefreshToken>.Success(new ConsumedVoiceRefreshToken(login.Id, login.TenantId, token.FamilyId, login.Email)));
        }

        public Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default)
        {
            Calls++;
            var token = Tokens.FirstOrDefault(t => t.Token.TokenHash == tokenHash);
            if (token.Token is not null)
                for (var j = 0; j < Tokens.Count; j++)
                    if (Tokens[j].Token.FamilyId == token.Token.FamilyId) Tokens[j] = (Tokens[j].Token, Tokens[j].Used, true);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private static VoiceTenant MakeTenant(string status) => new(
        Tenant, "Dag Plumbing", "plumbing", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", null, "owner@example.com", "+15615550100", status, DateTimeOffset.UnixEpoch);

    private sealed class Rig
    {
        public Owners Owners { get; } = new();
        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
        public CapturingLogger<OwnerAuthService> Log { get; } = new();
        public ReceptionistTokenOptions Options { get; }
        public OwnerAuthService Service { get; }

        public Rig(string status = VoiceTenantStatuses.Active, string? key = null, bool keySet = true)
        {
            Options = ReceptionistTokenOptions.From(keySet ? key ?? Key : null);
            Service = new OwnerAuthService(
                new Store(Result<VoiceTenant>.Success(MakeTenant(status))), Owners, new OwnerPasswords(), new ReceptionistTokens(Options, Time), Log);
        }

        public async Task<OwnerSession> SignedInAsync()
        {
            Assert.True((await Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, " Owner@Example.com ", Password, "Dana"))).IsSuccess);
            var session = await Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));
            Assert.True(session.IsSuccess, session.Error);
            return session.Value!;
        }
    }

    private static async Task<ClaimsIdentity?> ValidateAsync(string token, ReceptionistTokenOptions options, DateTimeOffset now)
    {
        var parameters = ReceptionistAuth.ValidationParameters(options);
        parameters.LifetimeValidator = (notBefore, expires, _, _) => notBefore <= now.UtcDateTime && expires > now.UtcDateTime;
        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, parameters);
        return result.IsValid ? result.ClaimsIdentity : null;
    }

    [Fact]
    public async Task An_active_tenant_gets_one_login_and_only_a_hash_of_the_password_is_stored()
    {
        var rig = new Rig();

        var created = await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, " owner@example.com ", Password, "Dana"));

        Assert.True(created.IsSuccess, created.Error);
        var login = Assert.Single(rig.Owners.Logins);
        Assert.Equal(new OwnerLoginCreated(Tenant, login.Id), created.Value);
        Assert.Equal("owner@example.com", login.Email);
        Assert.DoesNotContain(Password, login.PasswordHash);
        Assert.True(new OwnerPasswords().Verify(login.PasswordHash, Password));
    }

    [Fact]
    public async Task A_tenant_that_is_not_active_gets_no_login_and_nothing_is_stored()
    {
        var rig = new Rig(status: VoiceTenantStatuses.PendingPayment);

        var result = await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "owner@example.com", Password, null));

        Assert.Equal(OwnerAuthFailureCodes.TenantNotActive, result.ErrorCode);
        Assert.Equal(0, rig.Owners.Calls);
    }

    [Fact]
    public async Task A_second_login_for_the_tenant_is_owner_exists()
    {
        var rig = new Rig();
        await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "owner@example.com", Password, null));

        var again = await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "other@example.com", Password, null));

        Assert.Equal(OwnerAuthFailureCodes.OwnerExists, again.ErrorCode);
    }

    public static TheoryData<CreateOwnerLoginRequest?> IncompleteCreates() => new()
    {
        null,
        new CreateOwnerLoginRequest(null, "a@b.co", Password, null),
        new CreateOwnerLoginRequest(Guid.Empty, "a@b.co", Password, null),
        new CreateOwnerLoginRequest(Tenant, null, Password, null),
        new CreateOwnerLoginRequest(Tenant, "   ", Password, null),
        new CreateOwnerLoginRequest(Tenant, "a@b.co", null, null),
        new CreateOwnerLoginRequest(Tenant, "a@b.co", "", null),
    };

    [Theory]
    [MemberData(nameof(IncompleteCreates))]
    public async Task An_incomplete_create_is_invalid_and_nothing_is_stored(CreateOwnerLoginRequest? request)
    {
        var rig = new Rig();

        var result = await rig.Service.CreateLoginAsync(request);

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(0, rig.Owners.Calls);
    }

    [Fact]
    public async Task Any_password_is_accepted_as_typed()
    {
        var rig = new Rig();

        var created = await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "owner@example.com", "a", null));
        var session = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", "a"));

        Assert.True(created.IsSuccess);
        Assert.True(session.IsSuccess);
    }

    [Fact]
    public async Task Signing_in_gives_a_token_signed_with_receptionists_key_carrying_the_owner_and_the_tenant()
    {
        var rig = new Rig();

        var session = await rig.SignedInAsync();

        var identity = await ValidateAsync(session.AccessToken, rig.Options, rig.Time.GetUtcNow());
        Assert.NotNull(identity);
        Assert.Equal(Tenant.ToString("D"), identity!.FindFirst(ReceptionistAuth.TenantClaim)?.Value);
        Assert.Equal(rig.Owners.Logins[0].Id.ToString("D"), identity.FindFirst(ReceptionistAuth.SubjectClaim)?.Value);
        Assert.Equal(Tenant, session.TenantId);
        Assert.Equal(rig.Time.GetUtcNow() + ReceptionistTokenOptions.AccessTokenLifetime, session.AccessTokenExpiresAt);
        var stored = Assert.Single(rig.Owners.Tokens);
        Assert.Equal(ReceptionistTokens.HashRefreshToken(session.RefreshToken), stored.Token.TokenHash);
        Assert.NotEqual(session.RefreshToken, stored.Token.TokenHash);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_refusal()
    {
        var rig = new Rig();
        await rig.SignedInAsync();

        var wrong = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", "not-it"));
        var unknown = await rig.Service.SignInAsync(new OwnerSignInRequest("nobody@example.com", Password));

        Assert.Equal(OwnerAuthFailureCodes.InvalidCredentials, wrong.ErrorCode);
        Assert.Equal(OwnerAuthFailureCodes.InvalidCredentials, unknown.ErrorCode);
        Assert.Equal(wrong.Error, unknown.Error);
        Assert.Single(rig.Owners.Tokens);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "not base64 at all")]
    [InlineData(true, "c2hvcnQ=")]
    public async Task Without_a_usable_key_sign_in_is_not_configured_and_nothing_is_read(bool keySet, string? key)
    {
        var rig = new Rig(key: key, keySet: keySet);
        await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "owner@example.com", Password, null));
        var before = rig.Owners.Calls;

        var result = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));

        Assert.Equal(OwnerAuthFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Equal(before, rig.Owners.Calls);
        Assert.Null(rig.Options.SigningKey);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_or_by_another_issuer_or_expired_is_refused()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();
        var now = rig.Time.GetUtcNow();

        var otherKey = ReceptionistTokenOptions.From(Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
        Assert.Null(await ValidateAsync(session.AccessToken, otherKey, now));
        Assert.Null(await ValidateAsync(session.AccessToken, ReceptionistTokenOptions.From(null), now));
        Assert.Null(await ValidateAsync(session.AccessToken, rig.Options, now + ReceptionistTokenOptions.AccessTokenLifetime + TimeSpan.FromSeconds(1)));

        var foreign = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://auth.geekatyourspot.com",
            Audience = ReceptionistTokenOptions.Audience,
            Expires = now.UtcDateTime.AddMinutes(5),
            NotBefore = now.UtcDateTime,
            SigningCredentials = new SigningCredentials(rig.Options.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), [ReceptionistAuth.TenantClaim] = Tenant.ToString() },
        });
        Assert.Null(await ValidateAsync(foreign, rig.Options, now));

        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ReceptionistTokenOptions.Issuer,
            Audience = ReceptionistTokenOptions.Audience,
            Expires = now.UtcDateTime.AddMinutes(5),
            NotBefore = now.UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), [ReceptionistAuth.TenantClaim] = Tenant.ToString() },
        });
        Assert.Null(await ValidateAsync(unsigned, rig.Options, now));
    }

    [Fact]
    public async Task A_refresh_token_is_spent_once_and_replaced_in_the_same_sign_in()
    {
        var rig = new Rig();
        var first = await rig.SignedInAsync();

        var second = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));

        Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.RefreshToken, second.Value!.RefreshToken);
        Assert.Equal(2, rig.Owners.Tokens.Count);
        Assert.Equal(rig.Owners.Tokens[0].Token.FamilyId, rig.Owners.Tokens[1].Token.FamilyId);
        Assert.NotNull(await ValidateAsync(second.Value.AccessToken, rig.Options, rig.Time.GetUtcNow()));
    }

    [Fact]
    public async Task A_refresh_token_presented_again_ends_the_whole_sign_in()
    {
        var rig = new Rig();
        var first = await rig.SignedInAsync();
        var second = (await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken))).Value!;

        var replay = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));
        var afterReplay = await rig.Service.RefreshAsync(new OwnerRefreshRequest(second.RefreshToken));

        Assert.Equal(OwnerAuthFailureCodes.RefreshTokenReused, replay.ErrorCode);
        Assert.Equal(OwnerAuthFailureCodes.RefreshTokenReused, afterReplay.ErrorCode);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused_and_signing_out_ends_the_sign_in()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();

        var unknown = await rig.Service.RefreshAsync(new OwnerRefreshRequest("made-up"));
        var signedOut = await rig.Service.SignOutAsync(new OwnerRefreshRequest(session.RefreshToken));
        var afterSignOut = await rig.Service.RefreshAsync(new OwnerRefreshRequest(session.RefreshToken));

        Assert.Equal(OwnerAuthFailureCodes.RefreshTokenInvalid, unknown.ErrorCode);
        Assert.True(signedOut.IsSuccess);
        Assert.Equal(OwnerAuthFailureCodes.RefreshTokenReused, afterSignOut.ErrorCode);
    }

    [Fact]
    public async Task No_password_or_token_is_ever_logged()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();
        await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", "wrong-" + Password));
        await rig.Service.RefreshAsync(new OwnerRefreshRequest(session.RefreshToken));
        await rig.Service.RefreshAsync(new OwnerRefreshRequest(session.RefreshToken));

        var all = string.Join("\n", rig.Log.Lines);
        Assert.DoesNotContain(Password, all);
        Assert.DoesNotContain(session.RefreshToken, all);
        Assert.DoesNotContain(session.AccessToken, all);
        Assert.DoesNotContain("owner@example.com", all);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(OwnerAuthFailureCodes.InvalidCredentials, 401)]
    [InlineData(OwnerAuthFailureCodes.RefreshTokenInvalid, 401)]
    [InlineData(OwnerAuthFailureCodes.RefreshTokenReused, 401)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(OwnerAuthFailureCodes.OwnerExists, 409)]
    [InlineData(OwnerAuthFailureCodes.TenantNotActive, 409)]
    [InlineData(OwnerAuthFailureCodes.NotConfigured, 503)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData(null, 503)]
    public void Each_code_has_its_status(string? code, int status) =>
        Assert.Equal(status, OwnerAuthController.StatusFor(code));

    [Fact]
    public async Task The_controller_answers_201_on_create_and_never_returns_the_password_or_its_hash()
    {
        var rig = new Rig();
        var controller = new OwnerAuthController(rig.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var created = Assert.IsAssignableFrom<ObjectResult>(await controller.Create(new CreateOwnerLoginRequest(Tenant, "owner@example.com", Password, null), default));

        Assert.Equal(201, created.StatusCode);
        Assert.IsType<OwnerLoginCreated>(created.Value);
    }

    [Fact]
    public void Every_owner_route_names_receptionists_scheme_and_none_names_geekoauths()
    {
        foreach (var controller in new[] { typeof(McpOAuthController), typeof(CalendarController) })
        {
            var schemes = controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.AuthenticationSchemes).ToList();
            Assert.Equal([ReceptionistAuth.Scheme], schemes);
        }

        var voiceControllers = typeof(OwnerAuthController).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(OwnerAuthController).Namespace && typeof(ControllerBase).IsAssignableFrom(t));
        foreach (var controller in voiceControllers)
        {
            var named = controller.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(controller.GetMethods().SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()))
                .Select(a => a.AuthenticationSchemes);
            Assert.DoesNotContain(JwtBearerDefaults.AuthenticationScheme, named);
        }
    }

    [Fact]
    public void The_registered_scheme_reads_receptionists_key_issuer_and_audience_and_only_hs256()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(ReceptionistTokenOptions.From(Key));
        services.AddLogging();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer().AddReceptionistBearer();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>().Get(ReceptionistAuth.Scheme);
        var defaults = provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync().Result;

        Assert.Equal(ReceptionistTokenOptions.Issuer, options.TokenValidationParameters.ValidIssuer);
        Assert.Equal(ReceptionistTokenOptions.Audience, options.TokenValidationParameters.ValidAudience);
        Assert.Equal([SecurityAlgorithms.HmacSha256], options.TokenValidationParameters.ValidAlgorithms);
        Assert.Single(options.TokenValidationParameters.IssuerSigningKeys);
        Assert.False(options.MapInboundClaims);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, defaults?.Name);
    }

    [Fact]
    public async Task The_store_client_keeps_refresh_token_reused_apart_from_a_conflict()
    {
        static HttpVoiceOwnerRepository Client(HttpStatusCode status, string body) =>
            new(new HttpClient(new Answer(status, body)) { BaseAddress = new Uri("https://repo.example.test/") }, NullLogger<HttpVoiceOwnerRepository>.Instance);

        var reused = await Client(HttpStatusCode.Conflict, "{\"error\":\"used\",\"code\":\"refresh_token_reused\"}").ConsumeRefreshTokenAsync("h");
        var conflict = await Client(HttpStatusCode.Conflict, "That email already has a login.").CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "h", null));
        var missing = await Client(HttpStatusCode.NotFound, "No login has that email.").FindOwnerLoginByEmailAsync("a@b.co");
        var down = await Client(HttpStatusCode.Unauthorized, "").FindOwnerLoginByEmailAsync("a@b.co");

        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, reused.ErrorCode);
        Assert.Equal(VoiceFailureCodes.Conflict, conflict.ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, missing.ErrorCode);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, down.ErrorCode);
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
    }
}
