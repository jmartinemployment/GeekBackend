using System.Security.Cryptography;
using System.Text;

namespace GeekApplication.Infrastructure;

/// <summary>AES-256-GCM encryption for WordPress app passwords and OAuth tokens at rest.</summary>
public static class SeoCredentialProtector
{
    public static (byte[] Cipher, byte[] Iv, byte[] Tag) Encrypt(string plaintext)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return (cipher, nonce, tag);
    }

    public static string Decrypt(byte[] cipher, byte[] iv, byte[] tag)
    {
        var key = GetKey();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] GetKey()
    {
        var raw = Environment.GetEnvironmentVariable("GEEK_SEO_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "GEEK_SEO_ENCRYPTION_KEY must be set (base64-encoded 32-byte key) to store WordPress credentials.");
        }

        var key = Convert.FromBase64String(raw.Trim());
        if (key.Length != 32)
            throw new InvalidOperationException("GEEK_SEO_ENCRYPTION_KEY must decode to exactly 32 bytes.");
        return key;
    }
}
