using System.Security.Cryptography;
using System.Text;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social;

/// <summary>
/// AES-256-GCM for the platform tokens GeekSocial holds. GeekAPI encrypts before a token goes to
/// repo/social/* and decrypts after one comes back, so GeekRepository and its database hold
/// ciphertext only and the key lives in this service alone (GEEK_SOCIAL_ENCRYPTION_KEY, 32 bytes,
/// base64).
/// </summary>
/// <remarks>
/// Modeled on GeekRepository's GtmCredentialProtector, copied rather than referenced (AGENTS.md,
/// "copy, never reuse"), with its own key variable. Like that class it never throws on a missing
/// or malformed key: the service boots without the key and the first operation that needs it
/// answers a failed Result carrying <see cref="SocialFailureCodes.EncryptionUnavailable"/>, which
/// the caller reports to the operator. A token is never stored unencrypted and never partially
/// written, and no message here carries a token, a key or any part of either.
/// </remarks>
public sealed class SocialCredentialProtector
{
    public const string KeyVariable = "GEEK_SOCIAL_ENCRYPTION_KEY";

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly Func<string?> _readKey;

    public SocialCredentialProtector()
        : this(() => Environment.GetEnvironmentVariable(KeyVariable))
    {
    }

    /// <summary>Reads the key from <paramref name="readKey"/>, so a test supplies one without touching the environment.</summary>
    public SocialCredentialProtector(Func<string?> readKey) => _readKey = readKey;

    /// <summary>Encrypts a token for storage, carrying its expiry through unencrypted (a date is not a secret).</summary>
    public Result<SocialEncryptedToken> Protect(SocialToken token)
    {
        if (token is null)
            return Result<SocialEncryptedToken>.Failure("A token is required.", SocialFailureCodes.InvalidRequest);

        var key = ReadKey();
        if (key is null)
            return Result<SocialEncryptedToken>.Failure(KeyProblem, SocialFailureCodes.EncryptionUnavailable);

        var plain = Encoding.UTF8.GetBytes(token.Reveal());
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagBytes];
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plain, cipher, tag);
            return Result<SocialEncryptedToken>.Success(new SocialEncryptedToken
            {
                Cipher = cipher,
                Iv = nonce,
                Tag = tag,
                ExpiresAt = token.ExpiresAt,
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Decrypts a stored token. A ciphertext that fails its authentication tag does not belong to
    /// this key (the wrong key, or a row that was altered), and there is no token to return.
    /// </summary>
    public Result<SocialToken> Unprotect(SocialEncryptedToken stored)
    {
        if (stored is null || stored.Cipher.Length == 0 || stored.Iv.Length != NonceBytes || stored.Tag.Length != TagBytes)
            return Result<SocialToken>.Failure("The stored token is not well formed.", SocialFailureCodes.InvalidRequest);

        var key = ReadKey();
        if (key is null)
            return Result<SocialToken>.Failure(KeyProblem, SocialFailureCodes.EncryptionUnavailable);

        var plain = new byte[stored.Cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(stored.Iv, stored.Cipher, stored.Tag, plain);

            var value = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(value)
                ? Result<SocialToken>.Failure("The stored token decrypted to nothing.", SocialFailureCodes.EncryptionUnavailable)
                : Result<SocialToken>.Success(new SocialToken(value, stored.ExpiresAt));
        }
        catch (AuthenticationTagMismatchException)
        {
            return Result<SocialToken>.Failure(
                "The stored token does not match the encryption key. Reconnect the account if the key was changed.",
                SocialFailureCodes.EncryptionUnavailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string KeyProblem =>
        $"{KeyVariable} is not set to a base64 value of exactly {KeyBytes} bytes, so social tokens cannot be protected or read.";

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
