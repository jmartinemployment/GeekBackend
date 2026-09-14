namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Kill switch for citeable Create evidence gates (quote verify, coverage, partner-mention, sourceRights).
/// Env <c>GCC_V2_CITEABLE_CREATE_V1</c> — default ON.
/// OFF = <b>fail closed</b> for content types that require the citeable gate (emergency stop).
/// Never skip ship gates and still succeed — that is a forbidden fallback ("degraded mode").
/// Feature name in plans: <c>GccV2CiteableCreateV1</c>.
/// </summary>
public static class GccV2CiteableCreateFlags
{
    public const string EnvVar = "GCC_V2_CITEABLE_CREATE_V1";

    public const string DisabledFailClosedMessage =
        "Citeable VALIDATE is disabled (GCC_V2_CITEABLE_CREATE_V1 off). Fail closed — will not skip ship gates.";

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
