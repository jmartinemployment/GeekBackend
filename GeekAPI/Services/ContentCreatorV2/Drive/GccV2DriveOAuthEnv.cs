namespace GeekAPI.Services.ContentCreatorV2.Drive;

/// <summary>
/// CC-owned Google OAuth env for Drive (read-only Knowledge ingest).
/// Falls back to GSC Google client id/secret when Drive-specific vars are unset;
/// redirect URI must be Drive-specific. Tokens use GEEK_CC_GSC_ENCRYPTION_KEY.
/// </summary>
public static class GccV2DriveOAuthEnv
{
    public static string ClientId =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("GEEK_CC_DRIVE_GOOGLE_CLIENT_ID"),
            Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_ID"));

    public static string ClientSecret =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("GEEK_CC_DRIVE_GOOGLE_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_SECRET"));

    public static string RedirectUri =>
        (Environment.GetEnvironmentVariable("GEEK_CC_DRIVE_GOOGLE_REDIRECT_URI") ?? "").Trim();

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && Gsc.GccV2GscCredentialProtector.IsConfigured();

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = (value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
        }

        return "";
    }
}
