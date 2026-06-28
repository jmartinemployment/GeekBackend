using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace GeekAPI.Services.SiteAnalyzer2;

internal static class SiteAnalyzer2Connection
{
    public static string? TryResolve()
    {
        var raw = Environment.GetEnvironmentVariable("SITE_ANALYZER2_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Normalize(raw);
    }

    private static string Normalize(string rawValue)
    {
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
