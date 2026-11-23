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

    /// <summary>The OID of the NIST P-256 curve (secp256r1). ES256 means this curve, and another curve of the same size is not it.</summary>
    public const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>True only for a key on the P-256 curve itself; a 256-bit key on another curve (brainpoolP256r1, secp256k1) is not.</summary>
    public static bool IsP256(System.Security.Cryptography.ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.ExportParameters(false).Curve.Oid.Value == P256Oid;
    }

    /// <summary>GeekAPI's signing key: base64 PKCS#8. Set on GeekAPI only.</summary>
    public const string SigningKeyVariable = "GEEK_API_REPOSITORY_SIGNING_KEY";

    /// <summary>The matching public key: base64 SubjectPublicKeyInfo. Set on GeekRepository only.</summary>
    public const string PublicKeyVariable = "GEEK_API_PUBLIC_KEY";
}
