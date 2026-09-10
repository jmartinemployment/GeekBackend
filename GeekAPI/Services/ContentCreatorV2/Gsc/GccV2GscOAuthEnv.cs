namespace GeekAPI.Services.ContentCreatorV2.Gsc;

/// <summary>CC-owned Google OAuth env for Search Console (not Geek SEO vars).</summary>
public static class GccV2GscOAuthEnv
{
    public static string ClientId =>
        (Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_ID") ?? "").Trim();

    public static string ClientSecret =>
        (Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_SECRET") ?? "").Trim();

    public static string RedirectUri =>
        (Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_REDIRECT_URI") ?? "").Trim();

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && GccV2GscCredentialProtector.IsConfigured();
}
