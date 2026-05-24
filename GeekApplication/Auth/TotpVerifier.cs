using OtpNet;

namespace GeekApplication.Auth;

public static class TotpVerifier
{
    public static string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public static string BuildQrUri(string issuer, string accountName, string base32Secret) =>
        new OtpUri(OtpType.Totp, base32Secret, accountName, issuer).ToString();

    public static bool Verify(string base32Secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = code.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length is not (6 or 8))
            return false;

        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.VerifyTotp(normalized, out _, new VerificationWindow(window, window));
    }
}
