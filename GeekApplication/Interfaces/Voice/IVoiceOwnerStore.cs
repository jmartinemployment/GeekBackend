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

    /// <summary>Replaces a login's password hash. NotFound when the login does not exist.</summary>
    Task<Result<bool>> UpdateOwnerPasswordHashAsync(UpdateVoiceOwnerPasswordCommand command, CancellationToken ct = default);

    /// <summary>Stores the first refresh token of a new sign-in.</summary>
    Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default);

    /// <summary>
    /// Renews a sign-in: spends the presented token and stores its replacement in the same family, in one transaction that holds the
    /// owner's row, so a sign-out or a replay that ends the family cannot slip between the two and miss the replacement. NotFound when
    /// the hash is unknown or the token has expired. A token spent within <see cref="VoiceOwnerRefreshTokens.ReuseGrace"/> is renewed
    /// again. A token spent before that, or revoked, revokes its whole family and the code is
    /// <see cref="VoiceOwnerFailureCodes.RefreshTokenReused"/>.
    /// </summary>
    Task<Result<RotatedVoiceRefreshToken>> RotateRefreshTokenAsync(RotateVoiceRefreshTokenCommand command, CancellationToken ct = default);

    /// <summary>
    /// Ends the sign-in a refresh token belongs to by revoking its family, holding the owner's row as a renewal does, so a renewal in
    /// flight either finishes first and its replacement is revoked too, or finds the family revoked. An unknown hash is not an error:
    /// there is nothing to end.
    /// </summary>
    Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default);
}
