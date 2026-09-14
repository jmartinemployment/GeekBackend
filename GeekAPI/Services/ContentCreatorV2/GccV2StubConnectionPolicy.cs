namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>
/// Gates GSC/Drive/SharePoint stub connections. Default off. Process env only —
/// never request/header/body. Production/Staging refuse to enable stubs at startup.
/// </summary>
public static class GccV2StubConnectionPolicy
{
    public const string AllowEnvName = "GCC_V2_ALLOW_STUB_CONNECTIONS";
    public const string EnvironmentOverrideName = "GCC_V2_ENVIRONMENT";

    private static readonly object Gate = new();
    private static bool _validated;
    private static bool _allowed;

    /// <summary>True only when env flag is true and host is Development or e2e.</summary>
    public static bool AreStubsAllowed()
    {
        EnsureValidated();
        return _allowed;
    }

    /// <summary>
    /// Call once at host startup. In Production/Staging, a true flag fails startup.
    /// In Development/e2e, a true flag enables stubs and logs a warning via <paramref name="log"/>.
    /// </summary>
    public static void ValidateAtStartup(ILogger log)
    {
        lock (Gate)
        {
            var flag = IsFlagTrue();
            var localOrE2e = IsLocalOrE2eHost();
            if (flag && !localOrE2e)
            {
                throw new InvalidOperationException(
                    $"{AllowEnvName}=true is not permitted outside Development/e2e "
                    + $"(ASPNETCORE_ENVIRONMENT={Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(unset)"}, "
                    + $"{EnvironmentOverrideName}={Environment.GetEnvironmentVariable(EnvironmentOverrideName) ?? "(unset)"}).");
            }

            _allowed = flag && localOrE2e;
            _validated = true;
            if (_allowed)
            {
                log.LogWarning(
                    "Audit: stub OAuth connections enabled ({Env}=true on local/e2e host). "
                    + "Never enable in production.",
                    AllowEnvName);
            }
        }
    }

    /// <summary>Test hook — resets cached validation.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _validated = false;
            _allowed = false;
        }
    }

    private static void EnsureValidated()
    {
        if (_validated) return;
        lock (Gate)
        {
            if (_validated) return;
            // Controllers may run before hosted startup in tests — compute without throw.
            var flag = IsFlagTrue();
            var localOrE2e = IsLocalOrE2eHost();
            _allowed = flag && localOrE2e;
            _validated = true;
        }
    }

    private static bool IsFlagTrue()
    {
        var raw = (Environment.GetEnvironmentVariable(AllowEnvName) ?? "").Trim();
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
               || raw == "1";
    }

    private static bool IsLocalOrE2eHost()
    {
        var aspNet = (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "").Trim();
        if (string.Equals(aspNet, "Development", StringComparison.OrdinalIgnoreCase))
            return true;

        var overrideEnv = (Environment.GetEnvironmentVariable(EnvironmentOverrideName) ?? "").Trim();
        return string.Equals(overrideEnv, "e2e", StringComparison.OrdinalIgnoreCase)
               || string.Equals(overrideEnv, "local", StringComparison.OrdinalIgnoreCase);
    }
}
