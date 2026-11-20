using GeekAPI.Services.Voice.Storage;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// One connection a tenant can make, and everything about it that differs from every other: where its OAuth lives, what it asks for,
/// and the handful of places vendors disagree. The exchange itself is the same for all of them, which is why there is one OAuth client
/// and not one per vendor.
///
/// <para>
/// This was <c>GoogleConnection</c>, with Google's endpoints as constants on <see cref="GoogleCalendarOptions"/> and Google's
/// authorization parameters written into the client. That was honest while Google was the only thing connectable. It stops being
/// honest the moment a second vendor exists, so the parts that vary moved here and the client reads them.
/// </para>
/// </summary>
/// <param name="Provider">The stored value, and what a route names. Matches <c>VoiceMcpProviders</c>.</param>
/// <param name="DisplayName">What an owner is told they are connecting, and what a failure names.</param>
/// <param name="AuthorizeEndpoint">Where the owner's browser is sent to approve.</param>
/// <param name="TokenEndpoint">Where the one-time code is spent and the access token is renewed.</param>
/// <param name="Scopes">Every scope the connection needs. A person can untick one, so all of them must come back granted.</param>
/// <param name="ExternalId">What to save as the connection's external id: the calendar it books in, or nothing for storage.</param>
/// <param name="PartialGrantMessage">What the owner is told when they granted some of the scopes and not all.</param>
/// <param name="ClientIdVariable">The environment variable holding this vendor's OAuth client id.</param>
/// <param name="ClientSecretVariable">The environment variable holding this vendor's OAuth client secret.</param>
/// <param name="ExtraAuthorizationParameters">
/// Vendor-specific query parameters on the authorization address. Google needs <c>access_type=offline</c> and <c>prompt=consent</c> or
/// a second connection by the same person comes back with no refresh token; Microsoft needs neither, because it returns one whenever
/// <c>offline_access</c> is among the scopes.
/// </param>
/// <param name="RotatesRefreshToken">
/// True when the vendor returns a <b>new</b> refresh token on every renewal, which must then be stored in place of the old one.
/// Microsoft does this - verbatim, "Refresh tokens replace themselves with a fresh token upon every use" - and Google does not. A
/// renewal that quietly discarded the new token would work for ninety days and then strand the tenant, which is exactly the kind of
/// failure that does not show up in a test.
/// </param>
public sealed record McpProviderOAuth(
    string Provider,
    string DisplayName,
    string AuthorizeEndpoint,
    string TokenEndpoint,
    IReadOnlyList<string> Scopes,
    string? ExternalId,
    string PartialGrantMessage,
    string ClientIdVariable,
    string ClientSecretVariable,
    IReadOnlyDictionary<string, string> ExtraAuthorizationParameters,
    bool RotatesRefreshToken);

/// <summary>
/// The connections Geek-Receptionist offers. A provider is here only when something can actually be done with it: an entry whose
/// credentials are unset fails <see cref="McpFailureCodes.OAuthNotConfigured"/> and is never offered as a button that cannot complete.
/// </summary>
public static class McpProviders
{
    private static readonly IReadOnlyDictionary<string, string> GoogleAuthorizationParameters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Both are required together, every time: without them a second connection by the same person returns no refresh token at all
        // and the connection cannot be kept alive past the first hour.
        ["access_type"] = "offline",
        ["prompt"] = "consent",
        ["include_granted_scopes"] = "false",
    };

    public static readonly McpProviderOAuth Calendar = new(
        Provider: GoogleCalendarOptions.Provider,
        DisplayName: "Google Calendar",
        AuthorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint: "https://oauth2.googleapis.com/token",
        Scopes: GoogleCalendarOptions.Scopes,
        ExternalId: GoogleCalendarOptions.PrimaryCalendarId,
        PartialGrantMessage:
            "Calendar access was not fully granted. The receptionist needs every permission on the consent screen to check availability and book.",
        ClientIdVariable: "GOOGLE_OAUTH_CLIENT_ID",
        ClientSecretVariable: "GOOGLE_OAUTH_CLIENT_SECRET",
        ExtraAuthorizationParameters: GoogleAuthorizationParameters,
        RotatesRefreshToken: false);

    public static readonly McpProviderOAuth Drive = new(
        Provider: GoogleDriveOptions.Provider,
        DisplayName: "Google Drive",
        AuthorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint: "https://oauth2.googleapis.com/token",
        Scopes: [GoogleDriveOptions.FileScope],
        ExternalId: null,
        PartialGrantMessage: "Drive access was not granted, so call records have nowhere to go.",
        ClientIdVariable: "GOOGLE_OAUTH_CLIENT_ID",
        ClientSecretVariable: "GOOGLE_OAUTH_CLIENT_SECRET",
        ExtraAuthorizationParameters: GoogleAuthorizationParameters,
        RotatesRefreshToken: false);

    /// <summary>Every connection that can be made, in the order an owner should be offered them.</summary>
    public static readonly IReadOnlyList<McpProviderOAuth> All = [Calendar, Drive];

    /// <summary>The connection this provider names, or null for one this product cannot make.</summary>
    public static McpProviderOAuth? For(string provider) =>
        All.FirstOrDefault(c => string.Equals(c.Provider, provider, StringComparison.Ordinal));
}
