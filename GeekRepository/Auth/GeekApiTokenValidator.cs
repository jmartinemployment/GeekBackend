using GeekApplication.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeekRepository.Auth;

/// <summary>The outcome of checking a token: valid, or why not. The reason never repeats the token.</summary>
public readonly record struct GeekApiTokenCheck(bool IsValid, string Reason)
{
    public static GeekApiTokenCheck Valid { get; } = new(true, "valid");
    public static GeekApiTokenCheck Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Decides whether a request really comes from GeekAPI. The token must be signed by GeekAPI's key with ES256, issued by
/// <c>geek-api</c> for <c>geek-repository</c>, unexpired, carry a unique id it has not used before, and have been minted with a short
/// life. Anything else is refused: there is no second chance and no fallback.
/// </summary>
public sealed class GeekApiTokenValidator
{
    private readonly TokenValidationParameters _parameters;
    private readonly TokenReplayGuard _replay;
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new();

    public GeekApiTokenValidator(SecurityKey publicKey, TokenReplayGuard replay, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        _replay = replay;
        _time = time;
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = GeekRepositoryToken.Issuer,
            ValidateAudience = true,
            ValidAudience = GeekRepositoryToken.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicKey,
            ValidAlgorithms = [GeekRepositoryToken.Algorithm],
            ClockSkew = GeekRepositoryToken.ClockSkew,
        };
    }

    public async Task<GeekApiTokenCheck> CheckAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return GeekApiTokenCheck.Invalid("The token is empty.");

        var result = await _handler.ValidateTokenAsync(token, _parameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
            return GeekApiTokenCheck.Invalid($"The token was refused: {result.Exception?.GetType().Name ?? "invalid"}.");

        if (string.IsNullOrEmpty(jwt.Id))
            return GeekApiTokenCheck.Invalid("The token has no id (jti).");

        var issuedAt = new DateTimeOffset(DateTime.SpecifyKind(jwt.IssuedAt, DateTimeKind.Utc));
        var expires = new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc));
        if (jwt.IssuedAt == DateTime.MinValue)
            return GeekApiTokenCheck.Invalid("The token has no issue time (iat).");
        if (issuedAt > _time.GetUtcNow() + GeekRepositoryToken.ClockSkew)
            return GeekApiTokenCheck.Invalid("The token is issued in the future.");
        if (expires - issuedAt > GeekRepositoryToken.MaxLifetime)
            return GeekApiTokenCheck.Invalid("The token was minted with a longer life than GeekRepository accepts.");

        if (!_replay.TryRegister(jwt.Id, expires))
            return GeekApiTokenCheck.Invalid("The token has already been used.");

        return GeekApiTokenCheck.Valid;
    }
}
