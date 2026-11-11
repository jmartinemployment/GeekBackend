namespace GeekAPI.Auth;

/// <summary>
/// The scope guarding GeekSocial's routes and hub: the accounts, the inbox, posting and the audit.
/// GeekOAuth issues tokens for every Geek app and GeekAPI does not validate audience, so the scope is
/// what says this caller may act for the connected Pages. A token without it gets 403.
/// </summary>
public static class GeekSocialAuthConstants
{
    public const string ManageScope = "geeksocial.manage";

    /// <summary>The authorization policy name. Registered in Program.cs.</summary>
    public const string ManagePolicy = "GeekSocialManage";
}
