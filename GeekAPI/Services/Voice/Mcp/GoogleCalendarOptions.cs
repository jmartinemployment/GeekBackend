namespace GeekAPI.Services.Voice.Mcp;

/// <summary>Why connecting a tenant's calendar failed. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class McpFailureCodes
{
    public const string ProviderNotSupported = "provider_not_supported";
    public const string OAuthNotConfigured = "oauth_not_configured";
    public const string PortalNotConfigured = "portal_not_configured";
    public const string TenantUnverified = "tenant_unverified";
    public const string TenantForbidden = "tenant_forbidden";
    public const string TenantNotActive = "tenant_not_active";
    public const string StateInvalid = "state_invalid";
    public const string StateExpired = "state_expired";
    public const string AuthorizationDenied = "authorization_denied";
    public const string CodeExchangeFailed = "code_exchange_failed";
    public const string ScopeNotGranted = "scope_not_granted";
    public const string RefreshTokenMissing = "refresh_token_missing";
    public const string OAuthUnavailable = "oauth_unavailable";
}

/// <summary>
/// The Google OAuth client Geek-Receptionist uses to connect a tenant's calendar (Railway variables GOOGLE_OAUTH_CLIENT_ID and
/// GOOGLE_OAUTH_CLIENT_SECRET on GeekAPI, with PUBLIC_BASE_URL and PORTAL_BASE_URL). They are read once and checked when a
/// connection needs them, not at startup: an unset variable fails the connection with a logged error and a 503 and does not stop the
/// rest of GeekAPI from booting.
/// </summary>
public sealed record GoogleCalendarOptions(string? ClientId, string? ClientSecret, string? PublicBaseUrl, string? PortalBaseUrl)
{
    public const string Provider = "google_calendar";
    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>The scope booking needs: create, change and delete events.</summary>
    public const string EventsScope = "https://www.googleapis.com/auth/calendar.events";

    /// <summary>The three scopes the consent screen on geek-oauth-04-11-2026 lists, and no others.</summary>
    public static readonly IReadOnlyList<string> Scopes =
    [
        EventsScope,
        "https://www.googleapis.com/auth/calendar.events.freebusy",
        "https://www.googleapis.com/auth/calendar.calendarlist.readonly",
    ];

    /// <summary>The calendar the connection reads and writes: the signed-in person's own primary calendar.</summary>
    public const string PrimaryCalendarId = "primary";

    public static GoogleCalendarOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static GoogleCalendarOptions From(Func<string, string?> read) =>
        new(read("GOOGLE_OAUTH_CLIENT_ID")?.Trim(), read("GOOGLE_OAUTH_CLIENT_SECRET")?.Trim(),
            read("PUBLIC_BASE_URL")?.Trim().TrimEnd('/'), read("PORTAL_BASE_URL")?.Trim().TrimEnd('/'));

    /// <summary>The address Google sends the browser back to. It must match, character for character, one registered on the OAuth client.</summary>
    public string RedirectUri => $"{PublicBaseUrl}/api/v1/oauth/mcp/{Provider}/callback";

    /// <summary>The names of the variables the connection itself needs that are unset or blank. The portal address is checked on its own.</summary>
    public IReadOnlyList<string> MissingForOAuth()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("GOOGLE_OAUTH_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("GOOGLE_OAUTH_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(PublicBaseUrl)) missing.Add("PUBLIC_BASE_URL");
        return missing;
    }
}
