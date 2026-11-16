using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeekAPI.Services.Voice.OwnerAuth;

/// <summary>
/// The signing key of Geek-Receptionist's owner tokens (docs/Geek-Receptionist.md section 7), RECEPTIONIST_JWT_SIGNING_KEY: at least
/// 32 random bytes, base64. It belongs to Geek-Receptionist alone, so a token it signs is accepted by no other Geek app and no other
/// app's token is accepted here. A missing or malformed key does not stop GeekAPI from starting, because GeekAPI serves other products:
/// signing in answers 503 owner_auth_not_configured, and the owner scheme validates nothing, because it has no key to validate with.
/// </summary>
public sealed class ReceptionistTokenOptions
{
    public const string SigningKeyVariable = "RECEPTIONIST_JWT_SIGNING_KEY";
    public const string Issuer = "geek-receptionist";
    public const string Audience = "geek-receptionist-portal";

    /// <summary>The audience of a device token, which only says "this browser signed in as this owner"; never accepted as an access token.</summary>
    public const string DeviceAudience = "geek-receptionist-device";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan DeviceTokenLifetime = TimeSpan.FromDays(90);

    private const int MinimumKeyBytes = 32;

    private ReceptionistTokenOptions(SymmetricSecurityKey? signingKey, string? problem)
    {
        SigningKey = signingKey;
        Problem = problem;
    }

    /// <summary>Null when the key is missing or unusable; <see cref="Problem"/> then says which, never showing any part of the value.</summary>
    public SymmetricSecurityKey? SigningKey { get; }

    public string? Problem { get; }

    public static ReceptionistTokenOptions FromEnvironment() => From(Environment.GetEnvironmentVariable(SigningKeyVariable));

    public static ReceptionistTokenOptions From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ReceptionistTokenOptions(null, $"{SigningKeyVariable} is not set.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            return new ReceptionistTokenOptions(null, $"{SigningKeyVariable} is not base64.");
        }

        if (bytes.Length < MinimumKeyBytes)
            return new ReceptionistTokenOptions(null, $"{SigningKeyVariable} is shorter than {MinimumKeyBytes} bytes.");

        return new ReceptionistTokenOptions(new SymmetricSecurityKey(bytes), null);
    }
}

/// <summary>
/// The authentication scheme every owner route of Geek-Receptionist names, and the only one that reads an owner token. It is a second
/// JwtBearer scheme beside GeekOAuth's default one, with its own issuer, audience and key, and it is never the default, so it runs only on
/// a route that names it. HS256 is the only algorithm it accepts.
/// </summary>
public static class ReceptionistAuth
{
    public const string Scheme = "ReceptionistBearer";
    public const string TenantClaim = "tenant_id";
    public const string SubjectClaim = "sub";
    public const string EmailClaim = "email";

    public static AuthenticationBuilder AddReceptionistBearer(this AuthenticationBuilder builder)
    {
        builder.AddJwtBearer(Scheme, _ => { });
        builder.Services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<ReceptionistTokenOptions>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = ValidationParameters(tokens);
            });
        return builder;
    }

    /// <summary>What an owner token must be. With no usable key the list of keys is empty, so every token fails validation.</summary>
    public static TokenValidationParameters ValidationParameters(ReceptionistTokenOptions tokens) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = ReceptionistTokenOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = ReceptionistTokenOptions.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = tokens.SigningKey is null ? [] : [tokens.SigningKey],
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = SubjectClaim,
    };
}

/// <summary>A signed access token and when it stops being accepted.</summary>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>A signed device token and when it stops being accepted.</summary>
public sealed record IssuedDeviceToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>A refresh token as the owner holds it, and the SHA-256 hash that is all GeekRepository ever stores.</summary>
public sealed record IssuedRefreshToken(string Token, string Hash, DateTimeOffset ExpiresAt);

/// <summary>
/// Makes the tokens an owner signs in with. The access token is an HS256 JWT carrying the owner login (sub), the tenant (tenant_id) and
/// the email, good for <see cref="ReceptionistTokenOptions.AccessTokenLifetime"/>. The refresh token is 32 random bytes, base64url, with
/// no meaning of its own; it is stored only as its hash, so a copy of the database cannot be used to sign in. The device token is an
/// HS256 JWT for <see cref="ReceptionistTokenOptions.DeviceAudience"/> carrying only the owner login (sub), good for
/// <see cref="ReceptionistTokenOptions.DeviceTokenLifetime"/>: the portal keeps it in the browser after a sign-in, and a later sign-in
/// that carries it for the same login is not held back by the per-email cap (<see cref="OwnerSignInGuard"/>). It signs nobody in.
/// </summary>
public sealed class ReceptionistTokens(ReceptionistTokenOptions options, TimeProvider time)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public bool Configured => options.SigningKey is not null;

    public string? Problem => options.Problem;

    public IssuedAccessToken IssueAccessToken(Guid ownerLoginId, Guid tenantId, string email)
    {
        var key = options.SigningKey ?? throw new InvalidOperationException(options.Problem ?? "The owner token key is not configured.");
        var now = time.GetUtcNow();
        var expires = now + ReceptionistTokenOptions.AccessTokenLifetime;
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ReceptionistTokenOptions.Issuer,
            Audience = ReceptionistTokenOptions.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ReceptionistAuth.SubjectClaim] = ownerLoginId.ToString("D"),
                [ReceptionistAuth.TenantClaim] = tenantId.ToString("D"),
                [ReceptionistAuth.EmailClaim] = email,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        });
        return new IssuedAccessToken(token, expires);
    }

    public IssuedDeviceToken IssueDeviceToken(Guid ownerLoginId)
    {
        var key = options.SigningKey ?? throw new InvalidOperationException(options.Problem ?? "The owner token key is not configured.");
        var now = time.GetUtcNow();
        var expires = now + ReceptionistTokenOptions.DeviceTokenLifetime;
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ReceptionistTokenOptions.Issuer,
            Audience = ReceptionistTokenOptions.DeviceAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { [ReceptionistAuth.SubjectClaim] = ownerLoginId.ToString("D") },
        });
        return new IssuedDeviceToken(token, expires);
    }

    /// <summary>
    /// The owner login a device token was issued to, or null when there is no token or it is not one this service signed for the device
    /// audience and still within its life. A refused token is not an error: the sign-in simply gets no exemption.
    /// </summary>
    public async Task<Guid?> DeviceTokenOwnerAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || options.SigningKey is null) return null;

        var now = time.GetUtcNow().UtcDateTime;
        var parameters = ReceptionistAuth.ValidationParameters(options);
        parameters.ValidAudience = ReceptionistTokenOptions.DeviceAudience;
        parameters.LifetimeValidator = (notBefore, expires, _, p) =>
            expires is { } e && e + p.ClockSkew > now && (notBefore is not { } n || n - p.ClockSkew <= now);

        var result = await Handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        if (!result.IsValid) return null;
        return result.Claims.TryGetValue(ReceptionistAuth.SubjectClaim, out var sub) && Guid.TryParse(sub as string, out var id) ? id : null;
    }

    public IssuedRefreshToken IssueRefreshToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return new IssuedRefreshToken(token, HashRefreshToken(token), time.GetUtcNow() + ReceptionistTokenOptions.RefreshTokenLifetime);
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
