using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace GeekSa2Read;

/// <summary>
/// Resolves <c>SITE_ANALYZER2_DATABASE_URL</c> for read-only <c>sa2</c> access.
/// </summary>
public static class SiteAnalyzer2Connection
{
    public static string? TryResolve() =>
        Normalize(Environment.GetEnvironmentVariable("SITE_ANALYZER2_DATABASE_URL"));

    public static string Require()
    {
        var value = TryResolve();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("SITE_ANALYZER2_DATABASE_URL is required for Site Analyzer handoff reads.");
        return value;
    }

    internal static string? Normalize(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.ReplaceLineEndings("").Trim().Trim('"', '\'');
        if (!value.Contains("://", StringComparison.Ordinal))
            return value;

        try
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri))
                return value;
            if (databaseUri.Scheme is not ("postgres" or "postgresql"))
                return value;

            var userInfo = databaseUri.UserInfo.Split(':', 2);
            var username = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
            var database = databaseUri.AbsolutePath.Trim('/').Split('/', 2)[0];
            var query = QueryHelpers.ParseQuery(databaseUri.Query);
            query.TryGetValue("sslmode", out var sslModeValues);
            var sslMode = sslModeValues.Count > 0 ? sslModeValues[0] : null;
            if (string.IsNullOrWhiteSpace(sslMode) && query.TryGetValue("ssl_mode", out var sslModeAlt))
                sslMode = sslModeAlt.Count > 0 ? sslModeAlt[0] : null;

            var connBuilder = new NpgsqlConnectionStringBuilder
            {
                Host = databaseUri.Host,
                Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
                Username = username,
                Password = password,
                Database = database,
            };
            if (!string.IsNullOrWhiteSpace(sslMode)
                && Enum.TryParse<SslMode>(sslMode, true, out var parsedMode))
            {
                connBuilder.SslMode = parsedMode;
            }

            return connBuilder.ConnectionString;
        }
        catch
        {
            return value;
        }
    }
}
