namespace GeekAPI.Services.Social.Meta;

/// <summary>
/// The Meta app's settings, read once from environment variables on GeekAPI. The handshake and the
/// signature check answer from these values in memory and nothing else, so a value that is missing
/// is a failure the operator sees (not_configured), never a default.
/// </summary>
public sealed class MetaOptions
{
    public const string AppIdVariable = "GEEK_SOCIAL_META_APP_ID";
    public const string AppSecretVariable = "GEEK_SOCIAL_META_APP_SECRET";
    public const string VerifyTokenVariable = "GEEK_SOCIAL_META_VERIFY_TOKEN";
    public const string LoginConfigIdVariable = "GEEK_SOCIAL_META_LOGIN_CONFIG_ID";

    /// <summary>The Graph API version every call names. Pinned here so a version change is a reviewed edit.</summary>
    public const string GraphVersion = "v27.0";

    public string? AppId { get; init; }

    /// <summary>The app secret. It signs every webhook delivery and proves the app to the token endpoints. Never logged.</summary>
    public string? AppSecret { get; init; }

    /// <summary>The string typed into the Meta dashboard's webhook form; the handshake answers only a request that carries it.</summary>
    public string? VerifyToken { get; init; }

    /// <summary>The Facebook Login for Business configuration that names the permissions the connect screen asks for.</summary>
    public string? LoginConfigId { get; init; }

    public static MetaOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Reads each variable through <paramref name="read"/>, so a test supplies values without touching the environment.</summary>
    public static MetaOptions From(Func<string, string?> read) => new()
    {
        AppId = Clean(read(AppIdVariable)),
        AppSecret = Clean(read(AppSecretVariable)),
        VerifyToken = Clean(read(VerifyTokenVariable)),
        LoginConfigId = Clean(read(LoginConfigIdVariable)),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
