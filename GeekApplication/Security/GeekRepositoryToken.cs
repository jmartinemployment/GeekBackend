namespace GeekApplication.Security;

/// <summary>
/// What GeekAPI proves to GeekRepository on every request: a short-lived token signed with a key only GeekAPI holds, checked by a
/// public key only GeekRepository holds. These values are the contract between the two, so both read them from here.
/// </summary>
public static class GeekRepositoryToken
{
    public const string Issuer = "geek-api";
    public const string Audience = "geek-repository";

    /// <summary>ECDSA over P-256 with SHA-256. The only algorithm GeekRepository accepts, so a token cannot choose a weaker one.</summary>
    public const string Algorithm = "ES256";

    /// <summary>How long a token GeekAPI mints is valid.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>The longest <c>exp - iat</c> GeekRepository accepts, so a token minted with a long life is refused even when correctly signed.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(120);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    /// <summary>GeekAPI's signing key: base64 PKCS#8. Set on GeekAPI only.</summary>
    public const string SigningKeyVariable = "GEEK_API_REPOSITORY_SIGNING_KEY";

    /// <summary>The matching public key: base64 SubjectPublicKeyInfo. Set on GeekRepository only.</summary>
    public const string PublicKeyVariable = "GEEK_API_PUBLIC_KEY";
}
