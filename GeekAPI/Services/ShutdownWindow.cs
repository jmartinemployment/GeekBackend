namespace GeekAPI.Services;

/// <summary>
/// How long a deploy lets live work finish. Railway keeps the old instance for RAILWAY_DEPLOYMENT_DRAINING_SECONDS after SIGTERM and then
/// kills it; ASP.NET Core's own shutdown timeout is 30 seconds, which would cut a live phone call off long before that. The host is given
/// the drain period less a margin, so a call ends on its own, and its record is delivered, before Railway stops the process. Unset or
/// unreadable leaves ASP.NET Core's default.
/// </summary>
public static class ShutdownWindow
{
    public const string DrainVariable = "RAILWAY_DEPLOYMENT_DRAINING_SECONDS";

    /// <summary>What is kept back from Railway's drain period so the host stops before Railway does.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(10);

    public static TimeSpan? From(Func<string, string?> read) =>
        int.TryParse(read(DrainVariable)?.Trim(), out var seconds) && TimeSpan.FromSeconds(seconds) > Margin
            ? TimeSpan.FromSeconds(seconds) - Margin
            : null;
}
