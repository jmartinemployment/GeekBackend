using GeekAPI.Services.Voice.Storage;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// One kind of Google connection a tenant can make: what it is called, the scopes its consent screen asks for, and what to record as
/// the connection's external id. The OAuth client, the endpoints and the exchange are the same for all of them; only these differ.
/// </summary>
/// <param name="Provider">The provider the connection is stored under.</param>
/// <param name="Scopes">Every scope the connection needs. A person can untick one, so all of them must come back granted.</param>
/// <param name="ExternalId">What to save as the connection's external id: the calendar it books in, or nothing for storage.</param>
/// <param name="PartialGrantMessage">What the owner is told when they granted some of the scopes and not all.</param>
public sealed record GoogleConnection(string Provider, IReadOnlyList<string> Scopes, string? ExternalId, string PartialGrantMessage);

/// <summary>The Google connections Geek-Receptionist offers: the calendar it books in, and the Drive folder call records go to.</summary>
public static class GoogleConnections
{
    public static readonly GoogleConnection Calendar = new(
        GoogleCalendarOptions.Provider,
        GoogleCalendarOptions.Scopes,
        GoogleCalendarOptions.PrimaryCalendarId,
        "Calendar access was not fully granted. The receptionist needs every permission on the consent screen to check availability and book.");

    public static readonly GoogleConnection Drive = new(
        GoogleDriveOptions.Provider,
        [GoogleDriveOptions.FileScope],
        null,
        "Drive access was not granted, so call records have nowhere to go.");

    public static readonly IReadOnlyList<GoogleConnection> All = [Calendar, Drive];

    /// <summary>The connection this provider names, or null for one that is not a Google connection.</summary>
    public static GoogleConnection? For(string provider) =>
        All.FirstOrDefault(c => string.Equals(c.Provider, provider, StringComparison.Ordinal));
}
