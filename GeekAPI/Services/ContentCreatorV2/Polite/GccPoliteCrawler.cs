using System.Net;
using GeekApplication.Models.ContentCreator;
using TurnerSoftware.RobotsExclusionTools;

namespace GeekAPI.Services.ContentCreatorV2.Polite;

/// <summary>
/// Polite partner-destination crawler: robots.txt gate, per-host delay, 429/503 backoff.
/// Bot: <see cref="GccPartnerResearchCaps.UserAgent"/>.
/// </summary>
public sealed class GccV2PoliteCrawler : IGccV2PoliteCrawler
{
    private readonly HttpClient _http;
    private readonly GccV2PoliteHostRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly ILogger<GccV2PoliteCrawler> _logger;
    private readonly RobotsFileParser _robotsParser = new();
    private readonly TimeSpan _defaultHostDelay;
    private readonly TimeSpan? _hostDelayOverride;
    private readonly string _userAgent;

    /// <param name="hostDelayOverride">Tests only — e.g. 50ms so CI exercises delay without sleeping 12s.</param>
    public GccV2PoliteCrawler(
        HttpClient http,
        GccV2PoliteHostRegistry registry,
        TimeProvider clock,
        ILogger<GccV2PoliteCrawler> logger,
        TimeSpan? hostDelayOverride = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hostDelayOverride = hostDelayOverride;
        _defaultHostDelay = TimeSpan.FromSeconds(GccPartnerResearchCaps.DefaultHostDelaySeconds);
        _userAgent = GccPartnerResearchCaps.UserAgent;
    }

    public async Task<GccV2PoliteFetchResult> GetHtmlAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var origin = url.GetLeftPart(UriPartial.Authority);
        var controller = _registry.GetController(origin);

        return await controller.ExecutePolitelyAsync(async () =>
        {
            var robots = await EnsureRobotsAsync(origin, controller, cancellationToken).ConfigureAwait(false);
            var chosenDelay = ResolveChosenDelay(robots);

            if (robots is not null && !robots.IsAllowedAccess(url, _userAgent))
            {
                _logger.LogInformation(
                    "[partner crawl] Skipping url blocked by robots.txt: {Url}", url);
                return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.BlockedByRobots, null);
            }

            await controller.EnforceCooldownAsync(_clock, cancellationToken).ConfigureAwait(false);

            try
            {
                using var response = await _http.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var backoff = ResolveRetryAfter(response, chosenDelay);
                    _logger.LogWarning(
                        "[partner crawl] Rate limit ({Status}) for {Origin}. Cooldown {Seconds}s.",
                        (int)response.StatusCode, origin, backoff.TotalSeconds);
                    controller.ApplyExternalCooldown(backoff, _clock);
                    return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.RateLimited, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "[partner crawl] Soft-skipping HTTP {Status} for {Url}",
                        (int)response.StatusCode, url);
                    controller.MarkRequestCompleted(chosenDelay, _clock);
                    return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.HttpError, null);
                }

                var media = response.Content.Headers.ContentType?.MediaType ?? "";
                if (media.Length > 0
                    && !media.Contains("html", StringComparison.OrdinalIgnoreCase)
                    && !media.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
                    && !media.Contains("xml", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[partner crawl] Soft-skipping content-type {Media} for {Url}", media, url);
                    controller.MarkRequestCompleted(chosenDelay, _clock);
                    return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.ContentTypeSkipped, null);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var limited = new LimitedReadStream(stream, GccPartnerResearchCaps.MaxHtmlBytes);
                using var reader = new StreamReader(limited);
                var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                controller.MarkRequestCompleted(chosenDelay, _clock);

                if (string.IsNullOrWhiteSpace(html))
                {
                    _logger.LogWarning("[partner crawl] Empty body for {Url}", url);
                    return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.EmptyBody, null);
                }

                return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.Success, html);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[partner crawl] Request failed for {Url}", url);
                controller.MarkRequestCompleted(chosenDelay, _clock);
                return new GccV2PoliteFetchResult(GccV2PoliteFetchResult.Statuses.RequestFailed, null);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RobotsFile?> EnsureRobotsAsync(
        string origin,
        HostTrafficController controller,
        CancellationToken ct)
    {
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
                _logger.LogInformation(
                    "[partner crawl] robots.txt HTTP {Status} for {Origin}; defaulting to Allow.",
                    (int)response.StatusCode, origin);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                ex,
                "[partner crawl] Failed accessing robots.txt for {Origin}; defaulting to Allow.",
                origin);
        }

        // Stamp even on failure so robots→page (or next call) cannot burst.
        var delayAfterRobots = EffectiveSpacer(parsed);
        controller.MarkRequestCompleted(delayAfterRobots, _clock);
        _registry.SetRobots(origin, parsed);
        return parsed;
    }

    private TimeSpan ResolveChosenDelay(RobotsFile? robots) => EffectiveSpacer(robots);

    private TimeSpan EffectiveSpacer(RobotsFile? robots)
    {
        if (_hostDelayOverride is { } o)
            return o;

        var delay = _defaultHostDelay;
        if (robots is not null
            && robots.TryGetEntryForUserAgent(_userAgent, out var entry)
            && entry.CrawlDelay is { } cd
            && cd > 0)
        {
            var declared = TimeSpan.FromSeconds(cd);
            if (declared > delay)
                delay = declared;
        }

        return delay;
    }

    private static TimeSpan ResolveRetryAfter(HttpResponseMessage response, TimeSpan chosenDelay)
    {
        var fallback = chosenDelay * 5;
        try
        {
            var ra = response.Headers.RetryAfter;
            if (ra is null) return fallback;

            if (ra.Delta is { } delta && delta > TimeSpan.Zero)
                return delta;

            if (ra.Date is { } until)
            {
                var window = until - DateTimeOffset.UtcNow;
                if (window > TimeSpan.Zero)
                    return window;
            }
        }
        catch (Exception)
        {
            // Malformed Retry-After must not kill the job.
        }

        return fallback;
    }

    /// <summary>Stops reading after <paramref name="maxBytes"/> to avoid huge downloads.</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _read;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(count, _maxBytes - _read);
            var n = _inner.Read(buffer, offset, remaining);
            _read += n;
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(count, _maxBytes - _read);
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, remaining), cancellationToken).ConfigureAwait(false);
            _read += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(buffer.Length, _maxBytes - _read);
            var n = await _inner.ReadAsync(buffer[..remaining], cancellationToken).ConfigureAwait(false);
            _read += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
