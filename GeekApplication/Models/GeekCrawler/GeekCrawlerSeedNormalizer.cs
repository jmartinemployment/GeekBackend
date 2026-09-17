using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeekApplication.Models.GeekCrawler;

/// <summary>Seeds admitted for crawling, and the ones that were not, each with its reason.</summary>
public sealed record GeekCrawlerSeedAdmission(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<GeekCrawlerRejectedSeed> Rejected);

/// <summary>A seed that could not be admitted, and why.</summary>
public sealed record GeekCrawlerRejectedSeed(string Raw, string Reason);

/// <summary>
/// SSRF-safe crawl URL admission: schemes/ports, private IP rejection, DNS checks,
/// and redirect re-validation helpers for Geek-Crawler fetches.
/// </summary>
public static partial class GeekCrawlerSeedNormalizer
{
    public static readonly HashSet<int> AllowedPorts = new() { 80, 443 };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<string> NormalizeSeeds(IEnumerable<string>? rawSeeds)
    {
        if (rawSeeds is null) return [];

        var urls = new List<string>();
        foreach (var raw in rawSeeds)
        {
            if (!TryNormalizeSeedUrl(raw, out var normalized)) continue;
            if (!urls.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                urls.Add(normalized);
        }

        return urls;
    }

    public static string NormalizeOriginAuthority(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return origin;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return $"{uri.Scheme}://{host}";
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupSeedsByOrigin(
        IReadOnlyList<string> seedUrls)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in seedUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var origin = NormalizeOriginAuthority(uri.GetLeftPart(UriPartial.Authority));
            if (!map.TryGetValue(origin, out var list))
            {
                list = [];
                map[origin] = list;
            }

            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool SeedUrlsMatch(string? seedUrlsJson, IReadOnlyList<string> expectedSeeds)
    {
        if (expectedSeeds.Count == 0) return false;
        List<string> stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<string>>(seedUrlsJson ?? "[]", JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return false;
        }

        if (stored.Count != expectedSeeds.Count) return false;
        var set = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);
        return expectedSeeds.All(u => set.Contains(u));
    }

    public static string SerializeSeeds(IReadOnlyList<string> seeds) =>
        JsonSerializer.Serialize(seeds, JsonOpts);

    public static string ComputeSeedKey(IReadOnlyList<string> normalizedSeeds)
    {
        var sorted = normalizedSeeds.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var json = SerializeSeeds(sorted);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns an error message when any raw seed is invalid; otherwise null.</summary>
    /// <summary>
    /// Admit the seeds that are usable and report the ones that are not. One bad URL in a list does
    /// not spoil the rest.
    ///
    /// This previously returned on the FIRST unusable seed and rejected the whole request, so a list
    /// of twelve with three problems cost three round trips to discover, and a cap breach masked any
    /// invalid URLs behind it entirely. Nothing was crawled in the meantime.
    ///
    /// The cap is applied to NORMALIZED, de-duplicated seeds rather than raw lines, so the same URL
    /// typed with and without a scheme counts once — the limit governs what will actually be
    /// crawled, not how much was typed.
    /// </summary>
    public static GeekCrawlerSeedAdmission AdmitSeeds(IEnumerable<string>? rawSeeds)
    {
        var accepted = new List<string>();
        var rejected = new List<GeekCrawlerRejectedSeed>();

        foreach (var raw in rawSeeds ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var trimmed = raw.Trim();
            if (!TryNormalizeSeedUrl(raw, out var normalized))
            {
                rejected.Add(new GeekCrawlerRejectedSeed(trimmed, DescribeSeedRejection(raw)));
                continue;
            }

            if (accepted.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                continue;

            if (accepted.Count >= GeekCrawlerCaps.MaxSeedsPerRequest)
            {
                rejected.Add(new GeekCrawlerRejectedSeed(
                    trimmed,
                    $"Over the limit of {GeekCrawlerCaps.MaxSeedsPerRequest} seed URLs per request."));
                continue;
            }

            accepted.Add(normalized);
        }

        return new GeekCrawlerSeedAdmission(accepted, rejected);
    }

    /// <summary>The reason a seed could not be admitted, for reporting rather than guessing.</summary>
    private static string DescribeSeedRejection(string raw)
    {
        var trimmed = StripListPrefix(raw.Trim());

        var scheme = SchemePattern().Match(trimmed);
        if (scheme.Success
            && !string.Equals(scheme.Groups[1].Value, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme.Groups[1].Value, "https", StringComparison.OrdinalIgnoreCase))
        {
            return "Only http and https schemes are allowed.";
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.StartsWith("//") ? "https:" + trimmed : "https://" + trimmed.TrimStart('/');
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return "Not a URL.";

        return IsAllowedCrawlUri(uri, out var reason) ? "Not a usable seed URL." : reason ?? "Not a usable seed URL.";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.-]*)://")]
    private static partial System.Text.RegularExpressions.Regex SchemePattern();

    public static bool TryNormalizeSeedUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        trimmed = StripListPrefix(trimmed);
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("//"))
                trimmed = "https:" + trimmed;
            else
                trimmed = "https://" + trimmed.TrimStart('/');
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (!IsAllowedCrawlUri(uri, out _)) return false;

        url = uri.GetLeftPart(UriPartial.Query);
        if (url.EndsWith('/') && uri.AbsolutePath == "/")
            url = url.TrimEnd('/');
        return true;
    }

    /// <summary>
    /// Validates scheme, port, host literals, and (optionally) resolved DNS addresses.
    /// Call again before connect and after every redirect hop.
    /// </summary>
    public static bool IsAllowedCrawlUri(Uri uri, out string? rejectReason, bool resolveDns = false)
    {
        rejectReason = null;
        if (uri.Scheme is not ("http" or "https"))
        {
            rejectReason = "Only http and https schemes are allowed.";
            return false;
        }

        var port = uri.IsDefaultPort
            ? (uri.Scheme == "https" ? 443 : 80)
            : uri.Port;
        if (!AllowedPorts.Contains(port))
        {
            rejectReason = $"Port {port} is not allowed for crawl fetches.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            rejectReason = "Host is required.";
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = "Loopback and metadata hosts are not allowed.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            if (IsDisallowedAddress(literal))
            {
                rejectReason = "Private, loopback, link-local, and metadata IP addresses are not allowed.";
                return false;
            }

            return true;
        }

        if (!uri.Host.Contains('.'))
        {
            rejectReason = "Host must be a public DNS name or public IP.";
            return false;
        }

        if (!resolveDns) return true;

        try
        {
            var addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            if (addresses.Length == 0)
            {
                rejectReason = "Host did not resolve.";
                return false;
            }

            foreach (var address in addresses)
            {
                if (IsDisallowedAddress(address))
                {
                    rejectReason = "Host resolves to a private, loopback, link-local, or metadata address.";
                    return false;
                }
            }
        }
        catch (SocketException)
        {
            rejectReason = "Host DNS resolution failed.";
            return false;
        }

        return true;
    }

    /// <summary>DNS + policy check for an already-normalized absolute URL (rebinding-safe gate).</summary>
    public static bool TryValidateResolvedCrawlUrl(string url, out string? rejectReason)
    {
        rejectReason = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            rejectReason = "URL is not absolute.";
            return false;
        }

        return IsAllowedCrawlUri(uri, out rejectReason, resolveDns: true);
    }

    public static bool IsDisallowedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.Equals(IPAddress.None)) return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 127.0.0.0/8
            if (bytes[0] == 127) return true;
            // 169.254.0.0/16 (link-local + cloud metadata)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 100.64.0.0/10 CGNAT
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
            // 192.0.0.0/24, 192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24 docs/test
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] <= 2) return true;
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;
            // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved
            if (bytes[0] >= 224) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            // Unique local fc00::/7
            var b = address.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return true;
            // IPv4-mapped already handled; discard unspecified
            if (b.All(x => x == 0)) return true;
        }

        return false;
    }

    private static string StripListPrefix(string trimmed)
    {
        if (trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return trimmed[2..].TrimStart();
        }

        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;
        if (i > 0 && i < trimmed.Length && trimmed[i] == '.' && i + 1 < trimmed.Length && trimmed[i + 1] == ' ')
            return trimmed[(i + 2)..].TrimStart();

        return trimmed;
    }
}
