using System.Net;
using System.Net.Sockets;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// SSRF gate for operator-supplied outbound page URLs (hierarchy / project-site fetch).
/// Blocks loopback, link-local/metadata, RFC1918, CGNAT, and non-http(s) schemes before Playwright navigates.
/// </summary>
public static class GccV2SafeOutboundUrl
{
    public delegate IPAddress[] HostResolver(string host);

    public static bool TryValidate(string? url, out Uri absolute, out string? rejectionReason) =>
        TryValidate(url, out absolute, out rejectionReason, resolve: null);

    public static bool TryValidate(
        string? url,
        out Uri absolute,
        out string? rejectionReason,
        HostResolver? resolve)
    {
        absolute = null!;
        rejectionReason = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            rejectionReason = "URL is required.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            rejectionReason = "URL must be absolute http(s).";
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            rejectionReason = "Only http and https URLs are allowed.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            rejectionReason = "URLs with embedded credentials are not allowed.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.DnsSafeHost) && string.IsNullOrWhiteSpace(uri.Host))
        {
            rejectionReason = "URL host is required.";
            return false;
        }

        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host))
            host = uri.Host;

        if (IsBlockedHostName(host))
        {
            rejectionReason = $"Host '{host}' is not allowed.";
            return false;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            if (IsBlockedAddress(literal))
            {
                rejectionReason = $"Address '{literal}' is not allowed.";
                return false;
            }

            absolute = uri;
            return true;
        }

        IPAddress[] addresses;
        try
        {
            var resolver = resolve ?? DefaultResolve;
            addresses = resolver(host) ?? [];
        }
        catch (Exception ex)
        {
            rejectionReason = $"DNS resolution failed for '{host}': {ex.Message}";
            return false;
        }

        if (addresses.Length == 0)
        {
            rejectionReason = $"Host '{host}' did not resolve.";
            return false;
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                rejectionReason = $"Host '{host}' resolves to blocked address '{address}'.";
                return false;
            }
        }

        absolute = uri;
        return true;
    }

    private static IPAddress[] DefaultResolve(string host) =>
        Dns.GetHostAddresses(host);

    internal static bool IsBlockedHostName(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized is "localhost" or "metadata" or "metadata.google.internal")
            return true;
        if (normalized.EndsWith(".localhost", StringComparison.Ordinal)
            || normalized.EndsWith(".local", StringComparison.Ordinal)
            || normalized.EndsWith(".internal", StringComparison.Ordinal))
            return true;
        return false;
    }

    internal static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.Broadcast))
            return true;

        if (address.IsIPv4MappedToIPv6)
            return IsBlockedAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            var bytes = address.GetAddressBytes();
            // Unique local addresses fc00::/7
            if ((bytes[0] & 0xfe) == 0xfc) return true;
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return true;

        var b = address.GetAddressBytes();
        if (b[0] == 0) return true; // 0.0.0.0/8
        if (b[0] == 10) return true; // 10.0.0.0/8
        if (b[0] == 127) return true; // loopback (belt + suspenders)
        if (b[0] == 169 && b[1] == 254) return true; // link-local / cloud metadata
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return true; // 172.16.0.0/12
        if (b[0] == 192 && b[1] == 168) return true; // 192.168.0.0/16
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return true; // CGNAT 100.64.0.0/10
        if (b[0] >= 224) return true; // multicast / reserved
        return false;
    }
}
