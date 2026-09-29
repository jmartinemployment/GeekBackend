using System.Security.Cryptography;
using System.Text;

namespace GeekRepository.Infrastructure;

/// <summary>AES-256-GCM encryption for Google Tag Manager OAuth refresh tokens at rest.</summary>
/// <remarks>
/// <para>
/// Copied from Geek-SEO's SeoCredentialProtector rather than referenced, per AGENTS.md
/// "copy, never reuse". Two deliberate differences from the original:
/// </para>
/// <para>
/// It does not throw. The original raised InvalidOperationException on a missing or malformed
/// key; CLAUDE.md section 2 forbids that, so every entry point returns null on failure and the
/// caller fails closed. A refresh token is never stored unencrypted and never partially written.
/// </para>
/// <para>
/// The key still comes from GEEK_SEO_ENCRYPTION_KEY. That name outlived the service it was named
/// for, but it is a live shared secret, not a label: Geek-GTM-MCP decrypts these same tokens with
/// the identically-named variable on its own side (see its README). Renaming it here alone would
/// silently break every existing connection, so the rename is a coordinated two-repo change and
/// has not been made.
/// </para>
/// </remarks>
public static class GtmCredentialProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/>, or returns null if the key is unusable.</summary>
    public static (byte[] Cipher, byte[] Iv, byte[] Tag)? Encrypt(string plaintext)
    {
        var key = GetKey();
        if (key is null)
            return null;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return (cipher, nonce, tag);
    }

    /// <summary>
    /// Decrypts a stored token, or returns null if the key is unusable or the ciphertext fails
    /// its authentication tag. A failed tag means the row does not match this key -- wrong key,
    /// or tampering -- and in both cases there is no token to return.
    /// </summary>
    public static string? Decrypt(byte[] cipher, byte[] iv, byte[] tag)
    {
        var key = GetKey();
        if (key is null)
            return null;

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        try
        {
            aes.Decrypt(iv, cipher, tag, plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>The 32-byte key, or null when it is absent or not exactly 32 bytes.</summary>
    private static byte[]? GetKey()
    {
        var raw = Environment.GetEnvironmentVariable("GEEK_SEO_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        Span<byte> decoded = stackalloc byte[33];
        if (!Convert.TryFromBase64String(raw.Trim(), decoded, out var written) || written != 32)
            return null;

        return decoded[..32].ToArray();
    }
}
