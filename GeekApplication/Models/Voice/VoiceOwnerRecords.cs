namespace GeekApplication.Models.Voice;

/// <summary>
/// The login of a tenant's one owner (docs/Geek-Receptionist.md section 7). The password hash is made by GeekAPI and stored as given; it
/// is returned only to GeekAPI, to be checked there, and never leaves it.
/// </summary>
public sealed record VoiceOwnerLogin(Guid Id, Guid TenantId, string Email, string PasswordHash, string? DisplayName);

/// <summary>The email is stored lower-cased. A tenant has one login and an email belongs to one login: either clash is a conflict.</summary>
public sealed record CreateVoiceOwnerLoginCommand(Guid TenantId, string Email, string PasswordHash, string? DisplayName);

/// <summary>A refresh token as GeekAPI stores it: only its hash. Every token of one sign-in shares a family, so a reused token can end the whole sign-in.</summary>
public sealed record StoreVoiceRefreshTokenCommand(Guid OwnerLoginId, Guid FamilyId, string TokenHash, DateTimeOffset ExpiresAt);

/// <summary>The sign-in a refresh token belonged to, returned when that token was valid and has just been spent.</summary>
public sealed record ConsumedVoiceRefreshToken(Guid OwnerLoginId, Guid TenantId, Guid FamilyId, string Email);

public static class VoiceOwnerFailureCodes
{
    /// <summary>A refresh token that was already spent or revoked was presented again; its whole family has been revoked.</summary>
    public const string RefreshTokenReused = "refresh_token_reused";
}
