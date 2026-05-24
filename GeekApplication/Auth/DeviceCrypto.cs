using System.Security.Cryptography;
using System.Text;

namespace GeekApplication.Auth;

public static class DeviceCrypto
{
    public static string ComputeFingerprint(string machineId, string biosUuid, string platform)
    {
        var payload = $"{machineId}|{biosUuid}|{platform}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifySignature(string publicKeyPem, string nonce, string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signatureBase64))
            return false;

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            var data = Encoding.UTF8.GetBytes(nonce);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
