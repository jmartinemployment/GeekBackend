namespace GeekAPI.Auth;

/// <summary>
/// The scope guarding Content Creator's project, client and billing rows.
/// </summary>
/// <remarks>
/// GeekOAuth issues tokens for every Geek app, so a valid token proves only that someone signed
/// in somewhere. Audience is not validated on this service today (see the JwtBearer setup in
/// Program.cs), which leaves the scope as the thing that actually says this caller was granted
/// this data — a token without it gets 403 rather than a project list.
///
/// The name follows the one custom scope GeekOAuth already registers, <c>devices.manage</c>:
/// resource, then action. It names the data, not the client, so it carries no version suffix even
/// though the client id that requests it is <c>geek-content-creator-v2</c>.
/// </remarks>
public static class ContentCreatorAuthConstants
{
    public const string ManageScope = "content-creator.manage";

    /// <summary>The authorization policy name. Registered in Program.cs.</summary>
    public const string ManagePolicy = "ContentCreatorManage";
}
