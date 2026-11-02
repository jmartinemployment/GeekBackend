using System.Net;
using GeekApplication.Models.Visitors;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// The IPs a report request asks to leave out, sent as a comma-separated <c>excludeIps</c> query
/// value. An entry that is not an IP address refuses the request rather than being skipped, so a
/// typo can never quietly leave the caller in their own report.
/// </summary>
public static class VisitorIpExclusion
{
    public static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    public static bool TryParse(string? raw, out IReadOnlySet<string> ips, out string? error)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in (raw ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // IPAddress.TryParse reads "12" as 0.0.0.12; a full dotted quad or an IPv6 form is required.
            var shaped = part.Contains(':') || part.Count(c => c == '.') == 3;
            if (!shaped || !IPAddress.TryParse(part, out var address))
            {
                ips = None;
                error = $"excludeIps holds '{part}', which is not an IP address.";
                return false;
            }
            set.Add(Normalize(address));
        }
        ips = set;
        error = null;
        return true;
    }

    /// <summary>True when the session's IP, or any IP it also used, is excluded.</summary>
    public static bool Matches(VisitorSession session, IReadOnlySet<string> excluded) =>
        excluded.Count > 0
        && (IsExcluded(session.Ip, excluded) || session.AdditionalIps.Any(ip => IsExcluded(ip, excluded)));

    private static bool IsExcluded(string? ip, IReadOnlySet<string> excluded)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        var trimmed = ip.Trim();
        return excluded.Contains(IPAddress.TryParse(trimmed, out var address) ? Normalize(address) : trimmed);
    }

    private static string Normalize(IPAddress address) =>
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
