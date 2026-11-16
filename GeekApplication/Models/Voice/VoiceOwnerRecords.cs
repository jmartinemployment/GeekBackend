namespace GeekApplication.Models.Voice;

/// <summary>
/// The login of a tenant's one owner (docs/Geek-Receptionist.md section 7). The password hash is made by GeekAPI and stored as given; it
/// is returned only to GeekAPI, to be checked there, and never leaves it.
/// </summary>
public sealed record VoiceOwnerLogin(Guid Id, Guid TenantId, string Email, string PasswordHash, string? DisplayName);

/// <summary>The email is stored lower-cased. A tenant has one login and an email belongs to one login: either clash is a conflict.</summary>
public sealed record CreateVoiceOwnerLoginCommand(Guid TenantId, string Email, string PasswordHash, string? DisplayName);

/// <summary>A login's new password hash, made by GeekAPI when the stored one was made at a lower cost than the hasher now uses.</summary>
public sealed record UpdateVoiceOwnerPasswordCommand(Guid OwnerLoginId, string PasswordHash);

/// <summary>A refresh token as GeekAPI stores it: only its hash. Every token of one sign-in shares a family, so a reused token can end the whole sign-in.</summary>
public sealed record StoreVoiceRefreshTokenCommand(Guid OwnerLoginId, Guid FamilyId, string TokenHash, DateTimeOffset ExpiresAt);

/// <summary>
/// One renewal: the token presented (its hash) is spent and its replacement is stored in the same family, in one transaction, so a
/// renewal is either wholly done or not done at all.
/// </summary>
public sealed record RotateVoiceRefreshTokenCommand(string OldTokenHash, string NewTokenHash, DateTimeOffset NewExpiresAt);

/// <summary>The sign-in a renewed refresh token belongs to, returned once its replacement is stored.</summary>
public sealed record RotatedVoiceRefreshToken(Guid OwnerLoginId, Guid TenantId, Guid FamilyId, string Email);

public static class VoiceOwnerRefreshTokens
{
    /// <summary>
    /// How long a token that was just renewed may be renewed again without ending the sign-in. Two requests from one browser that both
    /// find the access token expired present the same refresh token, and an answer lost after the renewal was saved leaves the browser
    /// holding the spent one; within this time either is renewed again. After it, a spent token presented again is a copy and ends the
    /// sign-in.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(60);
}

public static class VoiceOwnerFailureCodes
{
    /// <summary>A refresh token that was already spent or revoked was presented again; its whole family has been revoked.</summary>
    public const string RefreshTokenReused = "refresh_token_reused";
}
