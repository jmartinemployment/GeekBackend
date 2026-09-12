namespace GeekAPI.Services.ContentCreatorV2.SharePoint;

/// <summary>
/// CC-owned Microsoft OAuth env for SharePoint/OneDrive Knowledge ingest.
/// Tokens use GEEK_CC_GSC_ENCRYPTION_KEY (shared AES key with GSC/Drive).
/// </summary>
public static class GccV2SharePointOAuthEnv
{
    public static string ClientId =>
        (Environment.GetEnvironmentVariable("GEEK_CC_SHAREPOINT_CLIENT_ID") ?? "").Trim();

    public static string ClientSecret =>
        (Environment.GetEnvironmentVariable("GEEK_CC_SHAREPOINT_CLIENT_SECRET") ?? "").Trim();

    public static string RedirectUri =>
        (Environment.GetEnvironmentVariable("GEEK_CC_SHAREPOINT_REDIRECT_URI") ?? "").Trim();

    /// <summary>Azure AD tenant id, or "common" / "organizations" / "consumers".</summary>
    public static string Tenant
    {
        get
        {
            var tenant = (Environment.GetEnvironmentVariable("GEEK_CC_SHAREPOINT_TENANT") ?? "").Trim();
            return string.IsNullOrWhiteSpace(tenant) ? "common" : tenant;
        }
    }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && Gsc.GccV2GscCredentialProtector.IsConfigured();

    public static string AuthorizeEndpoint =>
        $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/authorize";

    public static string TokenEndpoint =>
        $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token";
}
