using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.OwnerAuth;

/// <summary>Why an owner login or sign-in was refused. The controller turns the code into a status; the wording is for people.</summary>
public static class OwnerAuthFailureCodes
{
    public const string TenantNotActive = "tenant_not_active";
    public const string OwnerExists = "owner_exists";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string RefreshTokenReused = VoiceOwnerFailureCodes.RefreshTokenReused;
    public const string NotConfigured = "owner_auth_not_configured";
}

/// <summary>
/// What the last step of /signup sends once the subscription is active (docs/Geek-Receptionist.md section 5, item 4). Every field is
/// nullable so a field that was left out is told apart from one that was sent empty.
/// </summary>
public sealed record CreateOwnerLoginRequest(Guid? TenantId, string? Email, string? Password, string? DisplayName);

public sealed record OwnerLoginCreated(Guid TenantId, Guid OwnerLoginId);

public sealed record OwnerSignInRequest(string? Email, string? Password);

public sealed record OwnerRefreshRequest(string? RefreshToken);

/// <summary>What a sign-in or a refresh hands the portal's server, which keeps both tokens in httpOnly cookies and never in the page.</summary>
public sealed record OwnerSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid TenantId,
    string Email);

/// <summary>
/// Geek-Receptionist's own owner accounts (docs/Geek-Receptionist.md section 7). No other Geek service is asked: the login is stored by
/// GeekRepository in geek_voice.owner_logins, the password is hashed here before it leaves GeekAPI, and the tokens are signed with
/// Geek-Receptionist's own key, so an owner's sign-in is good for Geek-Receptionist and nothing else.
/// <list type="bullet">
/// <item>Creating a login needs an active tenant, which only a verified PayPal subscription makes one, and a tenant has one login.</item>
/// <item>Signing in gives the same answer for an unknown email and a wrong password, and takes as long for either.</item>
/// <item>A refresh token is spent once and replaced (same sign-in, new token). One presented again ends that whole sign-in, because
/// a refresh token used twice means a copy of it exists somewhere.</item>
/// </list>
/// Nothing is retried. No password, token or hash is ever logged; an email is not logged either.
/// </summary>
public sealed class OwnerAuthService(
    IVoiceStore store,
    IVoiceOwnerStore owners,
    OwnerPasswords passwords,
    ReceptionistTokens tokens,
    ILogger<OwnerAuthService> logger)
{
    public async Task<Result<OwnerLoginCreated>> CreateLoginAsync(CreateOwnerLoginRequest? request, CancellationToken ct = default)
    {
        if (request?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return Refuse<OwnerLoginCreated>("A tenant is required.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.Email))
            return Refuse<OwnerLoginCreated>("An email is required.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrEmpty(request.Password))
            return Refuse<OwnerLoginCreated>("A password is required.", VoiceFailureCodes.InvalidRequest);

        var tenant = await store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
            return Result<OwnerLoginCreated>.Failure(tenant.Error ?? "The tenant could not be read.", tenant.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (tenant.Value!.Status != VoiceTenantStatuses.Active)
            return Refuse<OwnerLoginCreated>("An owner login can be created only after the subscription is active.", OwnerAuthFailureCodes.TenantNotActive);

        var created = await owners.CreateOwnerLoginAsync(
            new CreateVoiceOwnerLoginCommand(tenantId, request.Email.Trim(), passwords.Hash(request.Password), request.DisplayName),
            ct).ConfigureAwait(false);

        if (created.IsSuccess)
        {
            logger.LogInformation("Owner login {OwnerLoginId} created for tenant {TenantId}.", created.Value!.Id, tenantId);
            return Result<OwnerLoginCreated>.Success(new OwnerLoginCreated(tenantId, created.Value.Id));
        }

        if (created.ErrorCode == VoiceFailureCodes.Conflict)
            return Refuse<OwnerLoginCreated>("A login already exists for this account or this email. If you just created it, sign in with that email and password.", OwnerAuthFailureCodes.OwnerExists);

        return Result<OwnerLoginCreated>.Failure(created.Error ?? "The login could not be saved.", created.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    public async Task<Result<OwnerSession>> SignInAsync(OwnerSignInRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request.Password))
            return Refuse<OwnerSession>("An email and a password are required.", VoiceFailureCodes.InvalidRequest);

        var configured = CheckConfigured<OwnerSession>();
        if (configured is not null) return configured;

        var login = await owners.FindOwnerLoginByEmailAsync(request.Email.Trim(), ct).ConfigureAwait(false);
        if (!login.IsSuccess)
        {
            // An email with no login, or one that is not an email at all, is the same refusal as a wrong password.
            if (login.ErrorCode is VoiceFailureCodes.NotFound or VoiceFailureCodes.InvalidRequest || login.Status == ResultStatus.NotFound)
            {
                passwords.VerifyDecoy(request.Password);
                return WrongCredentials();
            }
            return Result<OwnerSession>.Failure(login.Error ?? "The login could not be read.", login.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        var owner = login.Value!;
        bool matches;
        try
        {
            matches = passwords.Verify(owner.PasswordHash, request.Password);
        }
        catch (FormatException)
        {
            logger.LogError("The stored password hash of owner login {OwnerLoginId} is not one this service made.", owner.Id);
            return Result<OwnerSession>.Failure("The login could not be checked.", VoiceFailureCodes.StoreUnavailable);
        }

        if (!matches)
        {
            logger.LogWarning("A sign-in to owner login {OwnerLoginId} was refused: wrong password.", owner.Id);
            return WrongCredentials();
        }

        var session = await StartAsync(owner.Id, owner.TenantId, owner.Email, Guid.NewGuid(), ct).ConfigureAwait(false);
        if (session.IsSuccess) logger.LogInformation("Owner login {OwnerLoginId} signed in.", owner.Id);
        return session;
    }

    public async Task<Result<OwnerSession>> RefreshAsync(OwnerRefreshRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            return Refuse<OwnerSession>("A refresh token is required.", OwnerAuthFailureCodes.RefreshTokenInvalid);

        var configured = CheckConfigured<OwnerSession>();
        if (configured is not null) return configured;

        var spent = await owners.ConsumeRefreshTokenAsync(ReceptionistTokens.HashRefreshToken(request.RefreshToken), ct).ConfigureAwait(false);
        if (!spent.IsSuccess)
        {
            if (spent.ErrorCode == VoiceOwnerFailureCodes.RefreshTokenReused)
                return Refuse<OwnerSession>("That sign-in has ended. Sign in again.", OwnerAuthFailureCodes.RefreshTokenReused);
            if (spent.ErrorCode is VoiceFailureCodes.NotFound or VoiceFailureCodes.InvalidRequest || spent.Status == ResultStatus.NotFound)
                return Refuse<OwnerSession>("That sign-in has expired. Sign in again.", OwnerAuthFailureCodes.RefreshTokenInvalid);
            return Result<OwnerSession>.Failure(spent.Error ?? "The sign-in could not be renewed.", spent.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        var token = spent.Value!;
        return await StartAsync(token.OwnerLoginId, token.TenantId, token.Email, token.FamilyId, ct).ConfigureAwait(false);
    }

    /// <summary>Ends the sign-in the refresh token belongs to. A token that is unknown is already no sign-in, so that is not an error.</summary>
    public async Task<Result<bool>> SignOutAsync(OwnerRefreshRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            return Refuse<bool>("A refresh token is required.", VoiceFailureCodes.InvalidRequest);

        var revoked = await owners.RevokeRefreshTokenFamilyAsync(ReceptionistTokens.HashRefreshToken(request.RefreshToken), ct).ConfigureAwait(false);
        if (revoked.IsSuccess) return Result<bool>.Success(true);
        return Result<bool>.Failure(revoked.Error ?? "The sign-in could not be ended.", revoked.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    /// <summary>Stores a new refresh token in <paramref name="familyId"/> and signs a new access token. The refresh token is stored first,
    /// so no session is handed out whose refresh token was never saved.</summary>
    private async Task<Result<OwnerSession>> StartAsync(Guid ownerLoginId, Guid tenantId, string email, Guid familyId, CancellationToken ct)
    {
        var refresh = tokens.IssueRefreshToken();
        var stored = await owners.StoreRefreshTokenAsync(
            new StoreVoiceRefreshTokenCommand(ownerLoginId, familyId, refresh.Hash, refresh.ExpiresAt), ct).ConfigureAwait(false);
        if (!stored.IsSuccess)
            return Result<OwnerSession>.Failure(stored.Error ?? "The sign-in could not be saved.", stored.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        var access = tokens.IssueAccessToken(ownerLoginId, tenantId, email);
        return Result<OwnerSession>.Success(new OwnerSession(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, tenantId, email));
    }

    private Result<T>? CheckConfigured<T>()
    {
        if (tokens.Configured) return null;
        logger.LogError("Owner sign-in is not possible: {Problem}", tokens.Problem);
        return Result<T>.Failure("Sign-in is not configured.", OwnerAuthFailureCodes.NotConfigured);
    }

    private Result<OwnerSession> WrongCredentials() =>
        Result<OwnerSession>.Failure("That email and password do not match a login.", OwnerAuthFailureCodes.InvalidCredentials);

    private Result<T> Refuse<T>(string error, string code)
    {
        logger.LogWarning("An owner login request was refused with {Code}.", code);
        return Result<T>.Failure(error, code);
    }
}
