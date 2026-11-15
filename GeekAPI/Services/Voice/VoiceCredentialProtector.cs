using System.Security.Cryptography;
using System.Text;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice;

/// <summary>
/// AES-256-GCM for the secrets Geek-Receptionist keeps for a tenant: the Twilio auth token and the calendar and CRM OAuth
/// tokens (docs/Geek-Receptionist.md section 8, VOICE_ENCRYPTION_KEY, 32 bytes, base64). GeekAPI encrypts before a value goes to
/// /internal/v1/* and decrypts after one comes back, so GeekRepository and its database hold ciphertext only and the key lives in
/// this service alone. The stored form is one base64 string: nonce (12 bytes), tag (16 bytes), then the cipher.
/// </summary>
/// <remarks>
/// Modeled on SocialCredentialProtector and copied rather than shared, with its own key variable. A missing or malformed key does
/// not stop the service from starting: the first operation that needs it fails with <see cref="EncryptionUnavailable"/>, and a
/// secret is never stored unencrypted. No message carries a secret, a key or any part of either.
/// </remarks>
/// <summary>
/// What a sealed value is for. The purpose is bound into the ciphertext (as AES-GCM associated data), so a value sealed for one purpose
/// cannot be opened as another: a sealed OAuth state cannot be passed off as a stored token, nor an access token as a refresh token.
/// </summary>
public static class VoiceSecretPurposes
{
    public const string McpOAuthState = "geek-voice/mcp-oauth-state";
    public const string McpAccessToken = "geek-voice/mcp-access-token";
    public const string McpRefreshToken = "geek-voice/mcp-refresh-token";
    public const string TwilioAuthToken = "geek-voice/twilio-auth-token";
    public const string KeyCheck = "geek-voice/key-check";
}

public sealed class VoiceCredentialProtector
{
    public const string KeyVariable = "VOICE_ENCRYPTION_KEY";
    public const string EncryptionUnavailable = "encryption_unavailable";
    public const string InvalidSecret = "invalid_secret";

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

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
            var stored = new byte[NonceBytes + TagBytes + plain.Length];
            var nonce = stored.AsSpan(0, NonceBytes);
            var tag = stored.AsSpan(NonceBytes, TagBytes);
            var cipher = stored.AsSpan(NonceBytes + TagBytes);
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
        if (bytes.Length <= NonceBytes + TagBytes)
            return Result<string>.Failure("The stored secret is not well formed.", InvalidSecret);

        var key = ReadKey();
        if (key is null)
            return Result<string>.Failure(KeyProblem, EncryptionUnavailable);

        var plain = new byte[bytes.Length - NonceBytes - TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(bytes.AsSpan(0, NonceBytes), bytes.AsSpan(NonceBytes + TagBytes), bytes.AsSpan(NonceBytes, TagBytes), plain, Encoding.UTF8.GetBytes(purpose));
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
