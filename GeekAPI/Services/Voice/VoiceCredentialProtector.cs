using System.Security.Cryptography;
using System.Text;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice;

/// <summary>
/// What a sealed value is for. The purpose is bound into the ciphertext (as AES-GCM associated data), so a value sealed for one purpose
/// cannot be opened as another: a sealed OAuth state cannot be passed off as a stored token, nor an access token as a refresh token.
///
/// <para>
/// <b>Every kind of secret needs its own purpose, and a borrowed one is a hole.</b> The bucket keys in
/// <c>storage_connections</c> were sealed under <see cref="McpAccessToken"/> and <see cref="McpRefreshToken"/>, which meant a Google
/// OAuth ciphertext from <c>tenant_mcp_connections</c> would open as an AWS access key and the reverse - the one thing the purpose
/// exists to prevent. They have their own purposes now, and so does each of the four personal details the product stores.
/// </para>
/// </summary>
public static class VoiceSecretPurposes
{
    public const string McpOAuthState = "geek-voice/mcp-oauth-state";
    public const string McpAccessToken = "geek-voice/mcp-access-token";
    public const string McpRefreshToken = "geek-voice/mcp-refresh-token";
    public const string TwilioAuthToken = "geek-voice/twilio-auth-token";
    public const string KeyCheck = "geek-voice/key-check";

    /// <summary>The business's own bucket credentials (<c>storage_connections</c>), one purpose each.</summary>
    public const string StorageAccessKeyId = "geek-voice/storage-access-key-id";
    public const string StorageSecretAccessKey = "geek-voice/storage-secret-access-key";

    /// <summary>
    /// The four personal details the product stores: the business's own contact address and number, the number its receptionist
    /// transfers a caller to, and the address its messages are sent to. Not secrets in the credential sense, but they are the
    /// business's and its people's, and they are the only values in these tables that identify a person.
    /// </summary>
    public const string TenantContactEmail = "geek-voice/tenant-contact-email";
    public const string TenantContactPhone = "geek-voice/tenant-contact-phone";
    public const string ConfigTransferTargetNumber = "geek-voice/config-transfer-target-number";
    public const string ConfigMessageDestinationEmail = "geek-voice/config-message-destination-email";

    /// <summary>What a named person gave in the signup declaration: their own name and title, and the business's address.</summary>
    public const string SignupAttestorName = "geek-voice/signup-attestor-name";
    public const string SignupAttestorTitle = "geek-voice/signup-attestor-title";
    public const string SignupBusinessAddress = "geek-voice/signup-business-address";
    public const string SignupDeclarantAddress = "geek-voice/signup-declarant-address";
    public const string SignupDeclarantPhone = "geek-voice/signup-declarant-phone";
}

/// <summary>
/// AES-256-GCM for the secrets Geek-Receptionist keeps for a tenant: the Twilio auth token and the calendar and storage OAuth
/// tokens (docs/Geek-Receptionist.md section 8, VOICE_ENCRYPTION_KEY, 32 bytes, base64). GeekAPI encrypts before a value goes to
/// /internal/v1/* and decrypts after one comes back, so GeekRepository and its database hold ciphertext only and the key lives in
/// this service alone. The stored form is one base64 string: a format byte, the nonce (12 bytes), the tag (16 bytes), then the cipher.
/// </summary>
/// <remarks>
/// Modeled on SocialCredentialProtector and copied rather than shared, with its own key variable. A missing or malformed key does
/// not stop the service from starting: the first operation that needs it fails with <see cref="EncryptionUnavailable"/>, and a
/// secret is never stored unencrypted. No message carries a secret, a key or any part of either.
/// </remarks>
public sealed class VoiceCredentialProtector
{
    public const string KeyVariable = "VOICE_ENCRYPTION_KEY";
    public const string EncryptionUnavailable = "encryption_unavailable";
    public const string InvalidSecret = "invalid_secret";

    /// <summary>A sealed value whose first byte is a format this build does not know. It is refused, never guessed at.</summary>
    public const string UnsupportedVersion = "unsupported_secret_version";

    /// <summary>
    /// The format of the sealed value, written as its first byte. It exists so the key can be rotated one day: until this byte was
    /// here, nothing in a stored value said which key or layout wrote it, <see cref="Unprotect"/> had only the current key to try,
    /// and a changed key would have made every stored row unreadable with no way to tell that from tampering. Rotation itself is not
    /// built; this is what makes it possible to build without re-reading every row blind.
    /// </summary>
    private const byte Version1 = 1;

    private const int VersionBytes = 1;
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>Where each part starts in the sealed value: version, then nonce, then tag, then the cipher.</summary>
    private const int NonceOffset = VersionBytes;
    private const int TagOffset = VersionBytes + NonceBytes;
    private const int CipherOffset = VersionBytes + NonceBytes + TagBytes;

    private readonly Func<string?> _readKey;

    public VoiceCredentialProtector()
        : this(() => Environment.GetEnvironmentVariable(KeyVariable))
    {
    }

    /// <summary>Reads the key from <paramref name="readKey"/>, so a test supplies one without touching the environment.</summary>
    public VoiceCredentialProtector(Func<string?> readKey) => _readKey = readKey;

    public Result<string> Protect(string secret, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (string.IsNullOrEmpty(secret))
            return Result<string>.Failure("A secret is required.", InvalidSecret);

        var key = ReadKey();
        if (key is null)
            return Result<string>.Failure(KeyProblem, EncryptionUnavailable);

        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var stored = new byte[CipherOffset + plain.Length];
            stored[0] = Version1;
            var nonce = stored.AsSpan(NonceOffset, NonceBytes);
            var tag = stored.AsSpan(TagOffset, TagBytes);
            var cipher = stored.AsSpan(CipherOffset);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
            return Result<string>.Success(Convert.ToBase64String(stored));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// A ciphertext that fails its authentication tag does not belong to this key (the wrong key, or a row that was altered),
    /// and there is no secret to return.
    /// </summary>
    public Result<string> Unprotect(string stored, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(stored ?? string.Empty);
        }
        catch (FormatException)
        {
            return Result<string>.Failure("The stored secret is not well formed.", InvalidSecret);
        }
        if (bytes.Length <= CipherOffset)
            return Result<string>.Failure("The stored secret is not well formed.", InvalidSecret);

        // A format this build does not know is refused rather than read at the offsets of the one it does know. Guessing would open
        // a value at the wrong boundaries and report whatever came out.
        if (bytes[0] != Version1)
            return Result<string>.Failure(
                $"The stored secret is format {bytes[0]}, which this service cannot read.", UnsupportedVersion);

        var key = ReadKey();
        if (key is null)
            return Result<string>.Failure(KeyProblem, EncryptionUnavailable);

        var plain = new byte[bytes.Length - CipherOffset];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(bytes.AsSpan(NonceOffset, NonceBytes), bytes.AsSpan(CipherOffset), bytes.AsSpan(TagOffset, TagBytes), plain, Encoding.UTF8.GetBytes(purpose));
            return Result<string>.Success(Encoding.UTF8.GetString(plain));
        }
        catch (AuthenticationTagMismatchException)
        {
            return Result<string>.Failure(
                $"The stored secret does not match {KeyVariable}. A changed key cannot read what the old key wrote.",
                EncryptionUnavailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Whether the key is set and exactly 32 bytes. Unprotect answers <see cref="EncryptionUnavailable"/> both for an unusable key and
    /// for a ciphertext this key did not write, so a caller that must tell the service being unconfigured from a forged value asks here.
    /// </summary>
    public bool KeyIsUsable()
    {
        var key = ReadKey();
        if (key is null)
            return false;

        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    private static string KeyProblem =>
        $"{KeyVariable} is not set to a base64 value of exactly {KeyBytes} bytes, so tenant secrets cannot be protected or read.";

    /// <summary>The 32-byte key, or null when it is absent or not exactly 32 bytes.</summary>
    private byte[]? ReadKey()
    {
        var raw = _readKey();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        Span<byte> decoded = stackalloc byte[KeyBytes + 1];
        if (!Convert.TryFromBase64String(raw.Trim(), decoded, out var written) || written != KeyBytes)
        {
            CryptographicOperations.ZeroMemory(decoded);
            return null;
        }

        var key = decoded[..KeyBytes].ToArray();
        CryptographicOperations.ZeroMemory(decoded);
        return key;
    }
}
