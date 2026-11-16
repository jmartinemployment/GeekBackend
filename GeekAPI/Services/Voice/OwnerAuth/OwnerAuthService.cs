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
    public const string NotConfigured = "owner_auth_not_configured";

    /// <summary>The email has failed too many sign-ins this hour (<see cref="OwnerSignInGuard"/>); answered 429.</summary>
    public const string TooManyFailedSignIns = "too_many_failed_sign_ins";
}

/// <summary>
/// What the last step of /signup sends once the subscription is active (docs/Geek-Receptionist.md section 5, item 4). Every field is
/// nullable so a field that was left out is told apart from one that was sent empty.
/// </summary>
public sealed record CreateOwnerLoginRequest(Guid? TenantId, string? Email, string? Password, string? DisplayName);

public sealed record OwnerLoginCreated(Guid TenantId, Guid OwnerLoginId);

/// <summary>The device token is the one an earlier sign-in from this browser was given, when the portal has it; it may be absent.</summary>
public sealed record OwnerSignInRequest(string? Email, string? Password, string? DeviceToken = null);

public sealed record OwnerRefreshRequest(string? RefreshToken);

/// <summary>
/// What a sign-in or a refresh hands the portal's server, which keeps the tokens in httpOnly cookies and never in the page. The device
/// token outlives a sign-out: it is what lets this browser past the per-email cap next time.
/// </summary>
public sealed record OwnerSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid TenantId,
    string Email,
    string DeviceToken,
    DateTimeOffset DeviceTokenExpiresAt);

/// <summary>
/// Geek-Receptionist's own owner accounts (docs/Geek-Receptionist.md section 7). No other Geek service is asked: the login is stored by
/// GeekRepository in geek_voice.owner_logins, the password is hashed here before it leaves GeekAPI, and the tokens are signed with
/// Geek-Receptionist's own key, so an owner's sign-in is good for Geek-Receptionist and nothing else.
/// <list type="bullet">
/// <item>Creating a login needs an active tenant, which only a verified PayPal subscription makes one, and a tenant has one login.</item>
/// <item>Signing in gives the same answer for an unknown email and a wrong password, and takes as long for either. An email that has
/// failed too often this hour is refused without its password being checked, unless the sign-in comes from a browser that signed in to
/// that same login before (<see cref="OwnerSignInGuard"/>).</item>
/// <item>A refresh token is spent and replaced (same sign-in, new token) in one GeekRepository call, so a renewal is never half done.
/// One presented again within <see cref="VoiceOwnerRefreshTokens.ReuseGrace"/> is renewed again, because two requests from one browser
/// can present it together and an answer can be lost after the renewal was saved. After that it ends the whole sign-in, because a
/// refresh token used twice means a copy of it exists somewhere.</item>
/// </list>
/// Nothing is retried. No password, token or hash is ever logged; an email is not logged either.
/// </summary>
public sealed class OwnerAuthService(
    IVoiceStore store,
    IVoiceOwnerStore owners,
    OwnerPasswords passwords,
    ReceptionistTokens tokens,
    OwnerSignInGuard guard,
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
        {
            if (tenant.ErrorCode == VoiceFailureCodes.NotFound)
                return Refuse<OwnerLoginCreated>("No account has that id.", VoiceFailureCodes.NotFound);
            return StoreFailed<OwnerLoginCreated>(tenant.ErrorCode, "Your login could not be created because the account store did not answer. Try again in a moment.");
        }
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

        if (created.ErrorCode is VoiceFailureCodes.NotFound or VoiceFailureCodes.InvalidRequest)
            return Refuse<OwnerLoginCreated>(created.Error ?? "The login was refused.", created.ErrorCode);
        return StoreFailed<OwnerLoginCreated>(created.ErrorCode,
            "Your login could not be confirmed because the account store did not answer. It may have been created: sign in with that email and password before trying again.");
    }

    public async Task<Result<OwnerSession>> SignInAsync(OwnerSignInRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request.Password))
            return Refuse<OwnerSession>("An email and a password are required.", VoiceFailureCodes.InvalidRequest);

        var configured = CheckConfigured<OwnerSession>();
        if (configured is not null) return configured;

        var emailKey = OwnerSignInGuard.KeyFor(request.Email);
        var login = await owners.FindOwnerLoginByEmailAsync(request.Email.Trim(), ct).ConfigureAwait(false);
        if (!login.IsSuccess)
        {
            // An email with no login, or one that is not an email at all, is the same refusal as a wrong password, and is counted and
            // capped the same way, so neither answer tells which emails have a login.
            if (login.ErrorCode is VoiceFailureCodes.NotFound or VoiceFailureCodes.InvalidRequest)
            {
                if (guard.IsBlocked(emailKey)) return TooManyFailures();
                passwords.CheckDecoy(request.Password);
                guard.RecordFailure(emailKey);
                return WrongCredentials();
            }
            return StoreFailed<OwnerSession>(login.ErrorCode, SignInUnavailable);
        }

        var owner = login.Value!;
        var knownDevice = await tokens.DeviceTokenOwnerAsync(request.DeviceToken).ConfigureAwait(false) == owner.Id;
        if (!knownDevice && guard.IsBlocked(emailKey))
        {
            logger.LogWarning("A sign-in to owner login {OwnerLoginId} was refused unchecked: too many failed sign-ins this hour.", owner.Id);
            return TooManyFailures();
        }

        PasswordCheck check;
        try
        {
            check = passwords.Check(owner.PasswordHash, request.Password);
        }
        catch (FormatException)
        {
            logger.LogError("The stored password hash of owner login {OwnerLoginId} is not one this service made.", owner.Id);
            return Result<OwnerSession>.Failure("The login could not be checked.", VoiceFailureCodes.StoreUnavailable);
        }

        if (check == PasswordCheck.Wrong)
        {
            guard.RecordFailure(emailKey);
            logger.LogWarning("A sign-in to owner login {OwnerLoginId} was refused: wrong password.", owner.Id);
            return WrongCredentials();
        }

        if (check == PasswordCheck.RightButOutdated)
            await ReplaceOutdatedHashAsync(owner.Id, request.Password, ct).ConfigureAwait(false);

        var session = await StartAsync(owner.Id, owner.TenantId, owner.Email, ct).ConfigureAwait(false);
        if (session.IsSuccess) logger.LogInformation("Owner login {OwnerLoginId} signed in.", owner.Id);
        return session;
    }

    public async Task<Result<OwnerSession>> RefreshAsync(OwnerRefreshRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            return Refuse<OwnerSession>("A refresh token is required.", OwnerAuthFailureCodes.RefreshTokenInvalid);

        var configured = CheckConfigured<OwnerSession>();
        if (configured is not null) return configured;

        var replacement = tokens.IssueRefreshToken();
        var rotated = await owners.RotateRefreshTokenAsync(
            new RotateVoiceRefreshTokenCommand(ReceptionistTokens.HashRefreshToken(request.RefreshToken), replacement.Hash, replacement.ExpiresAt),
            ct).ConfigureAwait(false);
        if (!rotated.IsSuccess)
        {
            if (rotated.ErrorCode == VoiceOwnerFailureCodes.RefreshTokenReused)
                return Refuse<OwnerSession>("That sign-in has ended. Sign in again.", VoiceOwnerFailureCodes.RefreshTokenReused);
            if (rotated.ErrorCode is VoiceFailureCodes.NotFound or VoiceFailureCodes.InvalidRequest)
                return Refuse<OwnerSession>("That sign-in has expired. Sign in again.", OwnerAuthFailureCodes.RefreshTokenInvalid);
            return StoreFailed<OwnerSession>(rotated.ErrorCode, "The sign-in could not be renewed because the account store did not answer. Try again in a moment.");
        }

        var owner = rotated.Value!;
        return Result<OwnerSession>.Success(SessionFor(owner.OwnerLoginId, owner.TenantId, owner.Email, replacement));
    }

    /// <summary>Ends the sign-in the refresh token belongs to. A token that is unknown is already no sign-in, so that is not an error.</summary>
    public async Task<Result<bool>> SignOutAsync(OwnerRefreshRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            return Refuse<bool>("A refresh token is required.", VoiceFailureCodes.InvalidRequest);

        var revoked = await owners.RevokeRefreshTokenFamilyAsync(ReceptionistTokens.HashRefreshToken(request.RefreshToken), ct).ConfigureAwait(false);
        if (revoked.IsSuccess) return Result<bool>.Success(true);
        if (revoked.ErrorCode == VoiceFailureCodes.InvalidRequest)
            return Refuse<bool>(revoked.Error ?? "A refresh token is required.", VoiceFailureCodes.InvalidRequest);
        return StoreFailed<bool>(revoked.ErrorCode, "The sign-in could not be ended because the account store did not answer.");
    }

    /// <summary>Starts a new sign-in: stores its first refresh token in a new family, then signs an access token. The refresh token is
    /// stored first, so no session is handed out whose refresh token was never saved.</summary>
    private async Task<Result<OwnerSession>> StartAsync(Guid ownerLoginId, Guid tenantId, string email, CancellationToken ct)
    {
        var refresh = tokens.IssueRefreshToken();
        var stored = await owners.StoreRefreshTokenAsync(
            new StoreVoiceRefreshTokenCommand(ownerLoginId, Guid.NewGuid(), refresh.Hash, refresh.ExpiresAt), ct).ConfigureAwait(false);
        if (!stored.IsSuccess)
            return StoreFailed<OwnerSession>(stored.ErrorCode, SignInUnavailable);

        return Result<OwnerSession>.Success(SessionFor(ownerLoginId, tenantId, email, refresh));
    }

    private OwnerSession SessionFor(Guid ownerLoginId, Guid tenantId, string email, IssuedRefreshToken refresh)
    {
        var access = tokens.IssueAccessToken(ownerLoginId, tenantId, email);
        var device = tokens.IssueDeviceToken(ownerLoginId);
        return new OwnerSession(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, tenantId, email, device.Token, device.ExpiresAt);
    }

    private const string SignInUnavailable = "Sign-in is not available right now because the account store did not answer. Try again in a moment.";

    /// <summary>
    /// Replaces a hash made at a lower cost than the hasher now uses, while the password is at hand, so every login ends up at the current
    /// cost. A failure is logged and the sign-in goes on: the old hash still checks, and the next sign-in tries again.
    /// </summary>
    private async Task ReplaceOutdatedHashAsync(Guid ownerLoginId, string password, CancellationToken ct)
    {
        var updated = await owners.UpdateOwnerPasswordHashAsync(new UpdateVoiceOwnerPasswordCommand(ownerLoginId, passwords.Hash(password)), ct)
            .ConfigureAwait(false);
        if (updated.IsSuccess)
            logger.LogInformation("The password hash of owner login {OwnerLoginId} was replaced at the current cost.", ownerLoginId);
        else
            logger.LogError("The outdated password hash of owner login {OwnerLoginId} could not be replaced: {Code}.", ownerLoginId, updated.ErrorCode);
    }

    /// <summary>
    /// A store failure as the person sees it. The store's own wording names GeekRepository and its status, which the person can do nothing
    /// with; HttpVoiceOwnerRepository has already logged it. The code is kept, so the status stays the same.
    /// </summary>
    private static Result<T> StoreFailed<T>(string? code, string sentence) =>
        Result<T>.Failure(sentence, code ?? VoiceFailureCodes.StoreUnavailable);

    private Result<T>? CheckConfigured<T>()
    {
        if (tokens.Configured) return null;
        logger.LogError("Owner sign-in is not possible: {Problem}", tokens.Problem);
        return Result<T>.Failure("Sign-in is not configured.", OwnerAuthFailureCodes.NotConfigured);
    }

    private static Result<OwnerSession> TooManyFailures() =>
        Result<OwnerSession>.Failure(
            "Too many failed sign-ins for this email. Try again in an hour, or sign in from a browser you have signed in from before.",
            OwnerAuthFailureCodes.TooManyFailedSignIns);

    private Result<OwnerSession> WrongCredentials() =>
        Result<OwnerSession>.Failure("That email and password do not match a login.", OwnerAuthFailureCodes.InvalidCredentials);

    private Result<T> Refuse<T>(string error, string code)
    {
        logger.LogWarning("An owner login request was refused with {Code}.", code);
        return Result<T>.Failure(error, code);
    }
}
