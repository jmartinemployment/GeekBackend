using GeekApplication.Models.Visitors;
using UAParser;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// The raw user agent, kept, and what uap-csharp reads out of it. The device type comes from the
/// mobile client hint when a Chromium browser sent one and from the user agent otherwise.
/// </summary>
public sealed class VisitorUserAgentParser
{
    private static readonly Parser Parser = UAParser.Parser.GetDefault();

    public VisitorUserAgent Parse(string? userAgent, VisitorClientHints? hints)
    {
        var raw = (userAgent ?? string.Empty).Trim();
        if (raw.Length > 1024) raw = raw[..1024];
        if (raw.Length == 0)
            return new VisitorUserAgent { Raw = string.Empty, DeviceType = "unknown" };

        var client = Parser.Parse(raw);
        return new VisitorUserAgent
        {
            Raw = raw,
            Browser = Named(client.UA.Family),
            BrowserVersion = Version(client.UA.Major, client.UA.Minor, client.UA.Patch),
            Os = Named(client.OS.Family),
            OsVersion = Version(client.OS.Major, client.OS.Minor, client.OS.Patch),
            DeviceType = DeviceType(raw, hints),
            Brand = Named(client.Device.Brand),
            Model = Named(client.Device.Model),
        };
    }

    internal static string DeviceType(string userAgent, VisitorClientHints? hints)
    {
        if (string.Equals(hints?.Mobile?.Trim(), "?1", StringComparison.Ordinal)) return "mobile";

        var ua = userAgent;
        var hasMobile = ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase);
        var isIpad = ua.Contains("iPad", StringComparison.OrdinalIgnoreCase);
        var isTablet = ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase);
        var isAndroid = ua.Contains("Android", StringComparison.OrdinalIgnoreCase);
        if (isIpad || isTablet || (isAndroid && !hasMobile)) return "tablet";
        if (hasMobile || ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "mobile";
        if (string.Equals(hints?.Mobile?.Trim(), "?0", StringComparison.Ordinal)) return "desktop";
        return "desktop";
    }

    private static string? Named(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "Other", StringComparison.Ordinal) ? null : value;

    private static string? Version(string? major, string? minor, string? patch)
    {
        if (string.IsNullOrWhiteSpace(major)) return null;
        var parts = new List<string> { major };
        if (!string.IsNullOrWhiteSpace(minor)) parts.Add(minor);
        if (!string.IsNullOrWhiteSpace(patch)) parts.Add(patch);
        return string.Join('.', parts);
    }
}
