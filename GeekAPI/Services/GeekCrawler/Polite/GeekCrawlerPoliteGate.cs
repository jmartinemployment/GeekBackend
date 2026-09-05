using GeekApplication.Models.GeekCrawler;
using TurnerSoftware.RobotsExclusionTools;

namespace GeekAPI.Services.GeekCrawler.Polite;

/// <summary>Robots.txt gate and per-host delay before Playwright navigation.</summary>
public sealed class GeekCrawlerPoliteGate
{
    private readonly HttpClient _http;
    private readonly GeekCrawlerHostRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly ILogger<GeekCrawlerPoliteGate> _logger;
    private readonly RobotsFileParser _robotsParser = new();
    private readonly TimeSpan _defaultHostDelay;
    private readonly string _userAgent = GeekCrawlerCaps.UserAgent;

    public GeekCrawlerPoliteGate(
        HttpClient http,
        GeekCrawlerHostRegistry registry,
        TimeProvider clock,
        GeekCrawlerOptions options,
        ILogger<GeekCrawlerPoliteGate> logger)
    {
        _http = http;
        _registry = registry;
        _clock = clock;
        _logger = logger;
        _defaultHostDelay = TimeSpan.FromSeconds(options.HostDelaySeconds);
    }

    public sealed record PrepareResult(bool Allowed);

    public async Task<PrepareResult> PrepareFetchAsync(Uri url, CancellationToken ct)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        var controller = _registry.GetController(origin);
        var robots = await EnsureRobotsAsync(origin, controller, ct).ConfigureAwait(false);

        if (_registry.IsRobotsForbidden(origin))
        {
            _logger.LogInformation("[geek-crawler] Skipping url — robots.txt forbidden for {Origin}", origin);
            return new PrepareResult(false);
        }

        if (robots is not null && !robots.IsAllowedAccess(url, _userAgent))
        {
            _logger.LogInformation("[geek-crawler] Skipping url blocked by robots.txt: {Url}", url);
            return new PrepareResult(false);
        }

        await controller.EnforceCooldownAsync(_clock, ct).ConfigureAwait(false);
        return new PrepareResult(true);
    }

    public void CompleteFetch(Uri url, int statusCode)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        var controller = _registry.GetController(origin);
        _registry.TryGetRobots(origin, out var robots);
        var delay = EffectiveSpacer(robots);
        controller.MarkRequestCompleted(delay, _clock);
    }

    public void ApplyRateLimit(Uri url)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        var controller = _registry.GetController(origin);
        _registry.TryGetRobots(origin, out var robots);
        var backoff = EffectiveSpacer(robots) * 5;
        controller.ApplyExternalCooldown(backoff, _clock);
    }

    /// <summary>Checks cached robots rules; missing/unfetched robots defaults to Allow.</summary>
    public bool IsUrlAllowed(Uri url)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        if (_registry.IsRobotsForbidden(origin))
            return false;

        if (_registry.TryGetRobots(origin, out var robots) && robots is not null)
            return robots.IsAllowedAccess(url, _userAgent);

        return true;
    }

    public async Task EnsureRobotsForOriginAsync(string origin, CancellationToken ct)
    {
        var controller = _registry.GetController(origin);
        await EnsureRobotsAsync(origin, controller, ct).ConfigureAwait(false);
    }

    private async Task<RobotsFile?> EnsureRobotsAsync(
        string origin,
        GeekCrawlerHostTrafficController controller,
        CancellationToken ct)
    {
        if (_registry.IsRobotsForbidden(origin))
            return null;

        if (_registry.TryGetRobots(origin, out var cached))
            return cached;

        await controller.EnforceCooldownAsync(_clock, ct).ConfigureAwait(false);

        RobotsFile? parsed = null;
        try
        {
            var robotsUri = new Uri(new Uri(origin + "/", UriKind.Absolute), "/robots.txt");
            using var response = await _http.GetAsync(robotsUri, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var contents = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                parsed = _robotsParser.FromString(contents, new Uri(origin));
            }
            else
            {
                // WAF/bot filters often 403 robots.txt from datacenter IPs while still
                // serving pages (and publishing an Allow-heavy robots file). Treat any
                // non-success like a fetch failure: default Allow rather than block-all.
                _logger.LogInformation(
                    "[geek-crawler] robots.txt HTTP {StatusCode} for {Origin}; default Allow.",
                    (int)response.StatusCode,
                    origin);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   || !ct.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as TaskCanceledException (an OCE) with a live run token.
            // Treat like any other robots fetch failure: default Allow and keep crawling.
            _logger.LogInformation(ex, "[geek-crawler] robots.txt fetch failed for {Origin}; default Allow.", origin);
        }

        var delayAfterRobots = EffectiveSpacer(parsed);
        controller.MarkRequestCompleted(delayAfterRobots, _clock);
        _registry.SetRobots(origin, parsed);
        return parsed;
    }

    private TimeSpan EffectiveSpacer(RobotsFile? robots)
    {
        var delay = _defaultHostDelay;
        if (robots is not null
            && robots.TryGetEntryForUserAgent(_userAgent, out var entry)
            && entry.CrawlDelay is { } cd
            && cd > 0)
        {
            var declared = TimeSpan.FromSeconds(cd);
            if (declared > delay) delay = declared;
        }

        return delay;
    }
}
