using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using GeekAPI.Auth;
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
using Microsoft.AspNetCore.Mvc.Filters;
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
/// a refresh token is spent and replaced in one store call, one presented again within the reuse grace is renewed again and after it ends
/// the sign-in, an unset key refuses sign-in before anything is read, every owner route names Geek-Receptionist's scheme and not
/// GeekOAuth's, the owner and onboarding routes refuse a call without the verified X-API-Key, and no password or token is ever logged.
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
        public Result<VoiceTenant> Tenant { get; set; } = tenant;
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Tenant);
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

    /// <summary>
    /// The owner store as GeekRepository behaves: one login per tenant and per email; a renewal spends the presented token and stores its
    /// replacement together; a token spent within the reuse grace is renewed again; one spent before it, or revoked, ends the family.
    /// </summary>
    private sealed class Owners(TimeProvider time) : IVoiceOwnerStore
    {
        public List<VoiceOwnerLogin> Logins { get; } = new();
        public List<(StoreVoiceRefreshTokenCommand Token, DateTimeOffset? UsedAt, bool Revoked)> Tokens { get; } = new();
        public int Calls { get; private set; }

        /// <summary>When set, a renewal answers this and changes nothing, as GeekRepository does when its transaction does not commit.</summary>
        public Result<RotatedVoiceRefreshToken>? RotateFails { get; set; }

        /// <summary>When set, creating a login answers this and stores nothing.</summary>
        public Result<VoiceOwnerLogin>? CreateFails { get; set; }

        /// <summary>When set, replacing a password hash answers this and changes nothing.</summary>
        public Result<bool>? UpdatePasswordFails { get; set; }

        public Task<Result<bool>> UpdateOwnerPasswordHashAsync(UpdateVoiceOwnerPasswordCommand command, CancellationToken ct = default)
        {
            Calls++;
            if (UpdatePasswordFails is { } failure) return Task.FromResult(failure);
            var i = Logins.FindIndex(l => l.Id == command.OwnerLoginId);
            if (i < 0) return Task.FromResult(Result<bool>.Failure("No login.", VoiceFailureCodes.NotFound));
            Logins[i] = Logins[i] with { PasswordHash = command.PasswordHash };
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default)
        {
            Calls++;
            if (CreateFails is { } failure) return Task.FromResult(failure);
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
            Tokens.Add((command, null, false));
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<RotatedVoiceRefreshToken>> RotateRefreshTokenAsync(RotateVoiceRefreshTokenCommand command, CancellationToken ct = default)
        {
            Calls++;
            if (RotateFails is { } failure) return Task.FromResult(failure);

            var now = time.GetUtcNow();
            var i = Tokens.FindIndex(t => t.Token.TokenHash == command.OldTokenHash);
            if (i < 0) return Task.FromResult(Result<RotatedVoiceRefreshToken>.Failure("Unknown.", VoiceFailureCodes.NotFound));
            var (token, usedAt, revoked) = Tokens[i];
            if (revoked || (usedAt is { } used && used <= now - VoiceOwnerRefreshTokens.ReuseGrace))
            {
                for (var j = 0; j < Tokens.Count; j++)
                    if (Tokens[j].Token.FamilyId == token.FamilyId) Tokens[j] = (Tokens[j].Token, Tokens[j].UsedAt, true);
                return Task.FromResult(Result<RotatedVoiceRefreshToken>.Failure("Reused.", VoiceOwnerFailureCodes.RefreshTokenReused));
            }
            if (token.ExpiresAt <= now) return Task.FromResult(Result<RotatedVoiceRefreshToken>.Failure("Expired.", VoiceFailureCodes.NotFound));

            Tokens[i] = (token, usedAt ?? now, false);
            Tokens.Add((new StoreVoiceRefreshTokenCommand(token.OwnerLoginId, token.FamilyId, command.NewTokenHash, command.NewExpiresAt), null, false));
            var login = Logins.Single(l => l.Id == token.OwnerLoginId);
            return Task.FromResult(Result<RotatedVoiceRefreshToken>.Success(new RotatedVoiceRefreshToken(login.Id, login.TenantId, token.FamilyId, login.Email)));
        }

        public Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default)
        {
            Calls++;
            var token = Tokens.FirstOrDefault(t => t.Token.TokenHash == tokenHash);
            if (token.Token is not null)
                for (var j = 0; j < Tokens.Count; j++)
                    if (Tokens[j].Token.FamilyId == token.Token.FamilyId) Tokens[j] = (Tokens[j].Token, Tokens[j].UsedAt, true);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private static VoiceTenant MakeTenant(string status) => new(
        Tenant, "Dag Plumbing", "Plumbing, heating, & air-conditioning contractors", "238220", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", "owner@example.com", "+15615550100", status, DateTimeOffset.UnixEpoch);

    private sealed class Rig
    {
        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
        public Owners Owners { get; }
        public CapturingLogger<OwnerAuthService> Log { get; } = new();
        public ReceptionistTokenOptions Options { get; }
        public OwnerSignInGuard Guard { get; } = new();
        public Store Tenants { get; }
        public OwnerAuthService Service { get; }

        public Rig(string status = VoiceTenantStatuses.Active, string? key = null, bool keySet = true)
        {
            Owners = new Owners(Time);
            Options = ReceptionistTokenOptions.From(keySet ? key ?? Key : null);
            Service = new OwnerAuthService(
                Tenants = new Store(Result<VoiceTenant>.Success(MakeTenant(status))), Owners, new OwnerPasswords(), new ReceptionistTokens(Options, Time), Guard, Log);
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
        Assert.Equal(PasswordCheck.Right, new OwnerPasswords().Check(login.PasswordHash, Password));
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
    public async Task A_refresh_token_presented_again_after_the_grace_ends_the_whole_sign_in()
    {
        var rig = new Rig();
        var first = await rig.SignedInAsync();
        var second = (await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken))).Value!;
        rig.Time.Advance(VoiceOwnerRefreshTokens.ReuseGrace);

        var replay = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));
        var afterReplay = await rig.Service.RefreshAsync(new OwnerRefreshRequest(second.RefreshToken));

        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, replay.ErrorCode);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, afterReplay.ErrorCode);
    }

    [Fact]
    public async Task Two_requests_renewing_with_one_token_within_the_grace_both_stay_signed_in()
    {
        var rig = new Rig();
        var first = await rig.SignedInAsync();

        var a = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));
        rig.Time.Advance(VoiceOwnerRefreshTokens.ReuseGrace - TimeSpan.FromSeconds(1));
        var b = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));

        Assert.True(a.IsSuccess, a.Error);
        Assert.True(b.IsSuccess, b.Error);
        Assert.NotEqual(a.Value!.RefreshToken, b.Value!.RefreshToken);
        Assert.True((await rig.Service.RefreshAsync(new OwnerRefreshRequest(a.Value.RefreshToken))).IsSuccess);
        Assert.True((await rig.Service.RefreshAsync(new OwnerRefreshRequest(b.Value.RefreshToken))).IsSuccess);
        Assert.Single(rig.Owners.Tokens.Select(t => t.Token.FamilyId).Distinct());
    }

    [Fact]
    public async Task A_renewal_the_store_could_not_finish_leaves_the_token_usable_and_is_not_an_ended_sign_in()
    {
        var rig = new Rig();
        var first = await rig.SignedInAsync();
        rig.Owners.RotateFails = Result<RotatedVoiceRefreshToken>.Failure("GeekRepository timed out.", VoiceFailureCodes.StoreUnavailable);

        var failed = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));
        rig.Owners.RotateFails = null;
        var retried = await rig.Service.RefreshAsync(new OwnerRefreshRequest(first.RefreshToken));

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, failed.ErrorCode);
        Assert.Equal(503, OwnerAuthController.StatusFor(failed.ErrorCode));
        Assert.DoesNotContain("GeekRepository", failed.Error);
        Assert.True(retried.IsSuccess, retried.Error);
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
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, afterSignOut.ErrorCode);
    }

    [Fact]
    public async Task A_hash_made_at_a_lower_cost_is_replaced_at_sign_in_and_the_owner_is_signed_in()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        var cheap = new Microsoft.AspNetCore.Identity.PasswordHasher<OwnerPasswords>(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions { IterationCount = 1000 }));
        var outdated = cheap.HashPassword(null!, Password);
        rig.Owners.Logins[0] = rig.Owners.Logins[0] with { PasswordHash = outdated };
        Assert.Equal(PasswordCheck.RightButOutdated, new OwnerPasswords().Check(outdated, Password));

        var session = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));

        Assert.True(session.IsSuccess, session.Error);
        Assert.NotEqual(outdated, rig.Owners.Logins[0].PasswordHash);
        Assert.Equal(PasswordCheck.Right, new OwnerPasswords().Check(rig.Owners.Logins[0].PasswordHash, Password));
    }

    [Fact]
    public async Task A_hash_that_could_not_be_replaced_is_logged_and_the_owner_is_still_signed_in()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        var cheap = new Microsoft.AspNetCore.Identity.PasswordHasher<OwnerPasswords>(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions { IterationCount = 1000 }));
        rig.Owners.Logins[0] = rig.Owners.Logins[0] with { PasswordHash = cheap.HashPassword(null!, Password) };
        rig.Owners.UpdatePasswordFails = Result<bool>.Failure("GeekRepository timed out.", VoiceFailureCodes.StoreUnavailable);

        var session = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));

        Assert.True(session.IsSuccess, session.Error);
        Assert.Contains(rig.Log.Lines, line => line.Contains("could not be replaced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_login_whose_save_went_unanswered_tells_the_owner_to_try_signing_in_and_names_no_service()
    {
        var rig = new Rig();
        rig.Owners.CreateFails = Result<VoiceOwnerLogin>.Failure("GeekRepository timed out.", VoiceFailureCodes.StoreUnavailable);

        var created = await rig.Service.CreateLoginAsync(new CreateOwnerLoginRequest(Tenant, "owner@example.com", Password, null));

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, created.ErrorCode);
        Assert.Contains("sign in", created.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GeekRepository", created.Error);
    }

    private static async Task FailTimesAsync(Rig rig, string email, int times)
    {
        for (var i = 0; i < times; i++)
            Assert.Equal(OwnerAuthFailureCodes.InvalidCredentials, (await rig.Service.SignInAsync(new OwnerSignInRequest(email, "wrong-" + i))).ErrorCode);
    }

    [Fact]
    public async Task After_ten_failures_an_email_is_refused_even_with_the_right_password()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        await FailTimesAsync(rig, "owner@example.com", OwnerSignInGuard.FailuresPerWindow);

        var blocked = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));

        Assert.Equal(OwnerAuthFailureCodes.TooManyFailedSignIns, blocked.ErrorCode);
        Assert.Equal(429, OwnerAuthController.StatusFor(blocked.ErrorCode));
    }

    [Fact]
    public async Task The_cap_counts_an_email_however_it_is_typed()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        await FailTimesAsync(rig, " OWNER@example.com ", OwnerSignInGuard.FailuresPerWindow);

        Assert.Equal(OwnerAuthFailureCodes.TooManyFailedSignIns, (await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password))).ErrorCode);
    }

    [Fact]
    public async Task An_unknown_email_is_counted_and_capped_the_same_way()
    {
        var rig = new Rig();
        await FailTimesAsync(rig, "nobody@example.com", OwnerSignInGuard.FailuresPerWindow);

        Assert.Equal(OwnerAuthFailureCodes.TooManyFailedSignIns, (await rig.Service.SignInAsync(new OwnerSignInRequest("nobody@example.com", "x"))).ErrorCode);
    }

    [Fact]
    public async Task The_owners_own_browser_signs_in_while_the_email_is_capped()
    {
        var rig = new Rig();
        var earlier = await rig.SignedInAsync();
        await FailTimesAsync(rig, "owner@example.com", OwnerSignInGuard.FailuresPerWindow);

        var fromKnownBrowser = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password, earlier.DeviceToken));
        var wrongFromKnownBrowser = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", "wrong", earlier.DeviceToken));

        Assert.True(fromKnownBrowser.IsSuccess, fromKnownBrowser.Error);
        Assert.Equal(OwnerAuthFailureCodes.InvalidCredentials, wrongFromKnownBrowser.ErrorCode);
    }

    [Fact]
    public async Task Another_logins_device_token_a_forged_one_or_an_access_token_gives_no_exemption()
    {
        var rig = new Rig();
        var owner = await rig.SignedInAsync();
        var other = new VoiceOwnerLogin(Guid.NewGuid(), Guid.NewGuid(), "other@example.com", new OwnerPasswords().Hash(Password), null);
        rig.Owners.Logins.Add(other);
        var otherSession = (await rig.Service.SignInAsync(new OwnerSignInRequest("other@example.com", Password))).Value!;
        var forged = new ReceptionistTokens(ReceptionistTokenOptions.From(Convert.ToBase64String(new byte[32])), rig.Time)
            .IssueDeviceToken(rig.Owners.Logins[0].Id).Token;
        await FailTimesAsync(rig, "owner@example.com", OwnerSignInGuard.FailuresPerWindow);

        foreach (var token in new[] { otherSession.DeviceToken, forged, owner.AccessToken, "not-a-token" })
            Assert.Equal(OwnerAuthFailureCodes.TooManyFailedSignIns,
                (await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password, token))).ErrorCode);
    }

    [Fact]
    public async Task A_device_token_is_never_accepted_as_an_access_token()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();

        Assert.Null(await ValidateAsync(session.DeviceToken, rig.Options, rig.Time.GetUtcNow()));
        Assert.Equal(rig.Owners.Logins[0].Id, await new ReceptionistTokens(rig.Options, rig.Time).DeviceTokenOwnerAsync(session.DeviceToken));
    }

    [Fact]
    public async Task An_expired_device_token_gives_no_exemption()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();
        rig.Time.Advance(ReceptionistTokenOptions.DeviceTokenLifetime + TimeSpan.FromMinutes(1));

        Assert.Null(await new ReceptionistTokens(rig.Options, rig.Time).DeviceTokenOwnerAsync(session.DeviceToken));
    }

    [Fact]
    public async Task A_cancelled_owner_cannot_sign_in_and_a_wrong_password_there_gets_the_ordinary_refusal()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        rig.Tenants.Tenant = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.Cancelled));

        var right = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));
        var wrong = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", "wrong"));

        Assert.Equal(OwnerAuthFailureCodes.AccountCancelled, right.ErrorCode);
        Assert.Equal(OwnerAuthFailureCodes.InvalidCredentials, wrong.ErrorCode);
    }

    [Fact]
    public async Task A_cancelled_owners_renewal_is_refused_and_ends_the_sign_in()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();
        rig.Tenants.Tenant = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.Cancelled));

        var renewed = await rig.Service.RefreshAsync(new OwnerRefreshRequest(session.RefreshToken));

        Assert.Equal(OwnerAuthFailureCodes.AccountCancelled, renewed.ErrorCode);
        Assert.All(rig.Owners.Tokens, t => Assert.True(t.Revoked));
    }

    [Fact]
    public async Task A_suspended_owner_signs_in_and_renews()
    {
        var rig = new Rig();
        var session = await rig.SignedInAsync();
        rig.Tenants.Tenant = Result<VoiceTenant>.Success(MakeTenant(VoiceTenantStatuses.Suspended));

        Assert.True((await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password))).IsSuccess);
        Assert.True((await rig.Service.RefreshAsync(new OwnerRefreshRequest(session.RefreshToken))).IsSuccess);
    }

    [Fact]
    public async Task A_tenant_that_cannot_be_read_at_sign_in_is_the_store_failing_not_a_refusal_of_the_owner()
    {
        var rig = new Rig();
        await rig.SignedInAsync();
        rig.Tenants.Tenant = Result<VoiceTenant>.Failure("GeekRepository timed out.", VoiceFailureCodes.StoreUnavailable);

        var signIn = await rig.Service.SignInAsync(new OwnerSignInRequest("owner@example.com", Password));

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, signIn.ErrorCode);
        Assert.DoesNotContain("GeekRepository", signIn.Error);
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
        Assert.DoesNotContain(session.DeviceToken, all);
        Assert.DoesNotContain("owner@example.com", all);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(OwnerAuthFailureCodes.InvalidCredentials, 401)]
    [InlineData(OwnerAuthFailureCodes.RefreshTokenInvalid, 401)]
    [InlineData(VoiceOwnerFailureCodes.RefreshTokenReused, 401)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(OwnerAuthFailureCodes.TooManyFailedSignIns, 429)]
    [InlineData(OwnerAuthFailureCodes.AccountCancelled, 403)]
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

        var reused = await Client(HttpStatusCode.Conflict, "{\"error\":\"used\",\"code\":\"refresh_token_reused\"}").RotateRefreshTokenAsync(new RotateVoiceRefreshTokenCommand("old", "new", DateTimeOffset.UtcNow.AddDays(30)));
        var conflict = await Client(HttpStatusCode.Conflict, "That email already has a login.").CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "h", null));
        var missing = await Client(HttpStatusCode.NotFound, "No login has that email.").FindOwnerLoginByEmailAsync("a@b.co");
        var down = await Client(HttpStatusCode.Unauthorized, "").FindOwnerLoginByEmailAsync("a@b.co");

        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, reused.ErrorCode);
        Assert.Equal(VoiceFailureCodes.Conflict, conflict.ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, missing.ErrorCode);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, down.ErrorCode);
    }

    [Fact]
    public void The_owner_and_onboarding_routes_require_the_verified_api_key()
    {
        Assert.NotNull(typeof(OwnerAuthController).GetCustomAttribute<RequireApiKeyAttribute>());
        Assert.NotNull(typeof(OnboardingController).GetCustomAttribute<RequireApiKeyAttribute>());
    }

    [Fact]
    public void A_geekoauth_token_without_the_api_key_is_refused_and_a_verified_key_passes()
    {
        static AuthorizationFilterContext Context(HttpContext http) =>
            new(new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), []);

        var signedIn = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Bearer")) };
        var refused = Context(signedIn);
        new RequireApiKeyAttribute().OnAuthorization(refused);

        var keyed = new DefaultHttpContext();
        keyed.Items[CrmAuthConstants.VerifiedApiKeyItem] = true;
        var passed = Context(keyed);
        new RequireApiKeyAttribute().OnAuthorization(passed);

        var refusal = Assert.IsType<ObjectResult>(refused.Result);
        Assert.Equal(401, refusal.StatusCode);
        Assert.Contains(RequireApiKeyAttribute.FailureCode, System.Text.Json.JsonSerializer.Serialize(refusal.Value));
        Assert.Null(passed.Result);
    }

    [Fact]
    public async Task Both_receptionist_store_clients_read_a_refusal_the_same_way()
    {
        static HttpClient Http(HttpStatusCode status, string body) =>
            new(new Answer(status, body)) { BaseAddress = new Uri("https://repo.example.test/") };
        const string reused = "{\"error\":\"used\",\"code\":\"refresh_token_reused\"}";
        const string uncoded = "{\"error\":\"taken\"}";

        var tenants = new HttpVoiceRepository(Http(HttpStatusCode.Conflict, reused), NullLogger<HttpVoiceRepository>.Instance);
        var owners = new HttpVoiceOwnerRepository(Http(HttpStatusCode.Conflict, reused), NullLogger<HttpVoiceOwnerRepository>.Instance);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await tenants.GetTenantAsync(Tenant)).ErrorCode);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.FindOwnerLoginByEmailAsync("a@b.co")).ErrorCode);

        var plain = new HttpVoiceRepository(Http(HttpStatusCode.Conflict, uncoded), NullLogger<HttpVoiceRepository>.Instance);
        Assert.Equal(VoiceFailureCodes.Conflict, (await plain.GetTenantAsync(Tenant)).ErrorCode);
        var missingRoute = new HttpVoiceOwnerRepository(Http(HttpStatusCode.NotFound, ""), NullLogger<HttpVoiceOwnerRepository>.Instance);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, (await missingRoute.FindOwnerLoginByEmailAsync("a@b.co")).ErrorCode);
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
    }
}
