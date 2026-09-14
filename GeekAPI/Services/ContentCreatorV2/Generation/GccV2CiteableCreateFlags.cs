namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Kill switch for citeable Create M2 evidence gate (quote verify + section coverage).
/// Env <c>GCC_V2_CITEABLE_CREATE_V1</c> — default ON; set false/0/off to restore prior VALIDATE behavior.
/// Feature name in plans: <c>GccV2CiteableCreateV1</c>.
/// </summary>
public static class GccV2CiteableCreateFlags
{
    public const string EnvVar = "GCC_V2_CITEABLE_CREATE_V1";

    /// <summary>Test override; null = read env.</summary>
    internal static bool? OverrideEnabled { get; set; }

    public static bool IsCiteableCreateV1Enabled()
    {
        if (OverrideEnabled is bool forced) return forced;
        return ParseEnabledFlag(Environment.GetEnvironmentVariable(EnvVar));
    }

    private static bool ParseEnabledFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim() switch
        {
            "0" or "false" or "False" or "FALSE" or "no" or "off" => false,
            _ => true,
        };
    }
}
