using System.Net;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Whether a seed URL will actually be fetchable, as opposed to merely well-formed.
///
/// Syntax admission (<see cref="GeekCrawlerSeedNormalizer.AdmitSeeds"/>) cannot tell
/// <c>https://notarealdomain-xyz123.com</c> from a real partner site — both parse, both pass the
/// SSRF rules. That URL is admitted, crawled, and fails, and the operator finds out from a page
/// count that came up short. This is the check that answers "will this fail later".
/// </summary>
public sealed class GeekCrawlerSeedReachability
{
    private readonly HttpClient _http;
    private readonly ILogger<GeekCrawlerSeedReachability> _logger;

    public GeekCrawlerSeedReachability(
        HttpClient http,
        ILogger<GeekCrawlerSeedReachability> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SeedReachability>> CheckAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct)
    {
        var results = new List<SeedReachability>(urls.Count);
        foreach (var url in urls)
            results.Add(await CheckOneAsync(url, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<SeedReachability> CheckOneAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new SeedReachability(url, SeedReachabilityVerdict.Unreachable, "Not a URL.", null);

        // DNS first. A host that does not resolve is definitively broken and costs the target
        // nothing to establish, so it is settled before any request is made.
        if (!GeekCrawlerSeedNormalizer.IsAllowedCrawlUri(uri, out var dnsReason, resolveDns: true))
        {
            return new SeedReachability(
                url,
                SeedReachabilityVerdict.Unreachable,
                dnsReason ?? "Host did not resolve.",
                null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", GeekCrawlerCaps.UserAgent);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new SeedReachability(url, SeedReachabilityVerdict.Reachable, null, status);

            // A non-2xx is reported, not rejected. This request leaves Railway; the crawl leaves the
            // operator's machine. A 403 here is very often a bot manager reacting to a cloud IP and
            // says nothing about whether the local crawler will be served. Calling that "invalid"
            // would throw away good partner URLs.
            return new SeedReachability(
                url,
                SeedReachabilityVerdict.Questionable,
                $"Responded {status} {response.ReasonPhrase}. May still crawl from a local run; "
                + "check the URL if this is unexpected.",
                status);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SeedReachability(url, SeedReachabilityVerdict.Questionable, "Timed out.", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation("Seed reachability failed for {Url}: {Message}", url, ex.Message);
            return new SeedReachability(
                url,
                SeedReachabilityVerdict.Unreachable,
                $"Could not connect: {ex.Message}",
                null);
        }
    }
}

public enum SeedReachabilityVerdict
{
    /// <summary>Resolved and answered 2xx.</summary>
    Reachable,

    /// <summary>Resolved, but did not answer 2xx. Reported, not rejected.</summary>
    Questionable,

    /// <summary>Did not resolve or could not be connected to. This one will fail.</summary>
    Unreachable,
}

public sealed record SeedReachability(
    string Url,
    SeedReachabilityVerdict Verdict,
    string? Detail,
    int? StatusCode);
