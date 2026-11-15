using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// The owner logins and refresh tokens of Geek-Receptionist (docs/Geek-Receptionist.md section 7), kept apart from <see cref="IVoiceStore"/>
/// because they are looked up before any tenant is known. GeekAPI is the only caller, over /internal/v1/*.
/// </summary>
public interface IVoiceOwnerStore
{
    /// <summary>Conflict when the tenant already has a login or the email is taken; NotFound when the tenant does not exist.</summary>
    Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default);

    /// <summary>NotFound when no login has that email. The email is compared lower-cased.</summary>
    Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default);

    Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default);

    /// <summary>
    /// Spends a refresh token once, atomically. NotFound when the hash is unknown or the token has expired. When the token was already
    /// spent or was revoked, its whole family is revoked and the code is <see cref="VoiceOwnerFailureCodes.RefreshTokenReused"/>.
    /// </summary>
    Task<Result<ConsumedVoiceRefreshToken>> ConsumeRefreshTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Ends the sign-in a refresh token belongs to by revoking its family. An unknown hash is not an error: there is nothing to end.</summary>
    Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default);
}
