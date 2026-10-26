using GeekAPI.HttpClients;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// One beacon in, one or two writes out, then the score and, for a Florida session that crosses
/// the threshold, the intent mark on the session. Nothing is sent anywhere: the mark is read by
/// the daily report. Nothing here logs an IP address or a request body; a failure is logged with
/// the event kind and the store's answer and nothing else.
/// </summary>
public sealed class VisitorEventProcessor
{
    public const string BadRequest = "bad_request";
    public const string NotFound = "not_found";
    public const string StoreFailed = "store";
    public static readonly TimeSpan VisitorScoreWindow = VisitorDailyReportBuilder.VisitorScoreWindow;

    private const int MaxPathLength = 512;
    private const int MaxQueryLength = 512;
    private const int MaxTitleLength = 300;
    private const int MaxUrlLength = 2048;
    private const int MaxShortLength = 200;

    private readonly IVisitorRepositoryClient _repo;
    private readonly VisitorUserAgentParser _userAgents;
    private readonly TimeProvider _clock;
    private readonly ILogger<VisitorEventProcessor> _logger;

    public VisitorEventProcessor(
        IVisitorRepositoryClient repo,
        VisitorUserAgentParser userAgents,
        TimeProvider clock,
        ILogger<VisitorEventProcessor> logger)
    {
        _repo = repo;
        _userAgents = userAgents;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> ProcessAsync(VisitorPulseEvent e, CancellationToken ct)
    {
        if (e is null) return Result.Failure("An event is required.", BadRequest);
        if (!VisitorEventKinds.All.Contains(e.Kind)) return Result.Failure("Unknown event kind.", BadRequest);
        if (e.VisitorId == Guid.Empty || e.SessionId == Guid.Empty) return Result.Failure("visitorId and sessionId are required.", BadRequest);
        if (e.Request is null || string.IsNullOrWhiteSpace(e.Request.Ip)) return Result.Failure("request.ip is required.", BadRequest);
        var receivedAt = VisitorTime.Normalize(e.ReceivedAt == default ? _clock.GetUtcNow().UtcDateTime : e.ReceivedAt);

        Result<VisitorSession> written;
        var rescore = false;
        switch (e.Kind)
        {
            case VisitorEventKinds.PageView:
            {
                if (string.IsNullOrWhiteSpace(e.PageViewId) || string.IsNullOrWhiteSpace(e.Path))
                    return Result.Failure("pageViewId and path are required for a page view.", BadRequest);
                var geo = e.Request.Geo ?? new VisitorGeo();
                written = await _repo.PageViewAsync(new VisitorPageViewCommand
                {
                    SessionId = e.SessionId,
                    VisitorId = e.VisitorId,
                    ReceivedAt = receivedAt,
                    Page = new VisitorPageView
                    {
                        PageViewId = Cut(e.PageViewId, 64)!,
                        Path = Cut(e.Path, MaxPathLength)!,
                        Query = Cut(e.Query, MaxQueryLength),
                        Title = Cut(e.Title, MaxTitleLength),
                        EnteredAt = receivedAt,
                    },
                    Ip = e.Request.Ip.Trim(),
                    Geo = geo,
                    EdgeRegion = Cut(e.Request.EdgeRegion, 32),
                    Florida = IsFlorida(geo),
                    UserAgent = _userAgents.Parse(e.Request.UserAgent, e.Request.ClientHints),
                    ClientHints = e.Request.ClientHints,
                    AcceptLanguage = Cut(e.Request.AcceptLanguage, MaxShortLength),
                    Dnt = Cut(e.Request.Dnt, 4),
                    Snapshot = Trim(e.Snapshot),
                }, ct).ConfigureAwait(false);
                rescore = true;
                break;
            }
            case VisitorEventKinds.PageEnd:
                if (string.IsNullOrWhiteSpace(e.PageViewId)) return Result.Failure("pageViewId is required for a page end.", BadRequest);
                written = await _repo.PageEndAsync(e.SessionId, new VisitorPageEndCommand
                {
                    PageViewId = Cut(e.PageViewId, 64)!,
                    ActiveMs = Math.Clamp(e.ActiveMs ?? 0, 0, 24L * 60 * 60 * 1000),
                    MaxScrollPct = Math.Clamp(e.MaxScrollPct ?? 0, 0, 100),
                    ReceivedAt = receivedAt,
                }, ct).ConfigureAwait(false);
                rescore = true;
                break;
            case VisitorEventKinds.Click:
                if (string.IsNullOrWhiteSpace(e.PageViewId) || e.Click is null) return Result.Failure("pageViewId and click are required for a click.", BadRequest);
                written = await _repo.ClickAsync(e.SessionId, new VisitorClickCommand
                {
                    PageViewId = Cut(e.PageViewId, 64)!,
                    ReceivedAt = receivedAt,
                    Click = new VisitorPageClick
                    {
                        Href = Cut(e.Click.Href, MaxUrlLength),
                        LinkId = Cut(e.Click.LinkId, MaxShortLength),
                        Outbound = e.Click.Outbound,
                        At = receivedAt,
                    },
                }, ct).ConfigureAwait(false);
                break;
            case VisitorEventKinds.Vitals:
                if (string.IsNullOrWhiteSpace(e.PageViewId) || e.Vital is null) return Result.Failure("pageViewId and vital are required for vitals.", BadRequest);
                if (!VisitorVitalNames.All.Contains(e.Vital.Name?.Trim().ToUpperInvariant() ?? string.Empty))
                    return Result.Failure("vital.name must be LCP, INP, CLS, FCP or TTFB.", BadRequest);
                if (double.IsNaN(e.Vital.Value) || double.IsInfinity(e.Vital.Value) || e.Vital.Value < 0)
                    return Result.Failure("vital.value must be a non-negative number.", BadRequest);
                written = await _repo.VitalsAsync(e.SessionId, new VisitorVitalsCommand
                {
                    PageViewId = Cut(e.PageViewId, 64)!,
                    Name = e.Vital.Name!.Trim().ToUpperInvariant(),
                    Value = Math.Round(e.Vital.Value, 4),
                }, ct).ConfigureAwait(false);
                break;
            case VisitorEventKinds.Scheduler:
                if (!VisitorSchedulerSteps.All.Contains(e.SchedulerStep ?? string.Empty))
                    return Result.Failure("schedulerStep must be view, date_picked, slot_held or booked.", BadRequest);
                written = await _repo.SchedulerStepAsync(e.SessionId, new VisitorSchedulerStepCommand
                {
                    Step = e.SchedulerStep!,
                    At = receivedAt,
                }, ct).ConfigureAwait(false);
                rescore = e.SchedulerStep == VisitorSchedulerSteps.View;
                break;
            case VisitorEventKinds.Contact:
                written = await _repo.ContactSubmittedAsync(e.SessionId, new VisitorContactCommand { At = receivedAt }, ct).ConfigureAwait(false);
                break;
            case VisitorEventKinds.Geo:
            {
                if (e.Position is null) return Result.Failure("position is required for a geo event.", BadRequest);
                var lat = e.Position.Latitude;
                var lng = e.Position.Longitude;
                if (double.IsNaN(lat) || double.IsNaN(lng) || lat < -90 || lat > 90 || lng < -180 || lng > 180)
                    return Result.Failure("position must carry a latitude and a longitude.", BadRequest);
                var current = await _repo.GetSessionAsync(e.SessionId, ct).ConfigureAwait(false);
                if (!current.IsSuccess) return Map(current, e.Kind);
                var markFlorida = string.IsNullOrWhiteSpace(current.Value!.Geo?.Region) && VisitorIntentScoring.IsInFlorida(lat, lng);
                written = await _repo.BrowserGeoAsync(e.SessionId, new VisitorBrowserGeoCommand
                {
                    Position = new VisitorBrowserGeo
                    {
                        Latitude = Math.Round(lat, 5),
                        Longitude = Math.Round(lng, 5),
                        AccuracyMeters = e.Position.AccuracyMeters is { } acc && !double.IsNaN(acc) && acc >= 0 ? Math.Round(acc) : null,
                        At = receivedAt,
                    },
                    MarkFlorida = markFlorida,
                }, ct).ConfigureAwait(false);
                rescore = markFlorida;
                break;
            }
            default:
                return Result.Failure("Unknown event kind.", BadRequest);
        }

        if (!written.IsSuccess) return Map(written, e.Kind);
        if (!rescore) return Result.Success();
        return await RescoreAsync(written.Value!, ct).ConfigureAwait(false);
    }

    public async Task<Result> IdentifyAsync(VisitorIdentifyRequest request, CancellationToken ct)
    {
        if (request is null || request.VisitorId == Guid.Empty) return Result.Failure("visitorId is required.", BadRequest);
        if (string.IsNullOrWhiteSpace(request.Email)) return Result.Failure("email is required.", BadRequest);
        var source = request.Source == VisitorIdentitySources.Booking ? VisitorIdentitySources.Booking
            : request.Source == VisitorIdentitySources.Contact ? VisitorIdentitySources.Contact
            : null;
        if (source is null) return Result.Failure("source must be booking or contact.", BadRequest);

        var written = await _repo.IdentifyAsync(new VisitorIdentifyCommand
        {
            VisitorId = request.VisitorId,
            SessionId = request.SessionId is { } id && id != Guid.Empty ? id : null,
            Identity = new VisitorIdentity
            {
                FirstName = Cut(request.FirstName, MaxShortLength) ?? string.Empty,
                LastName = Cut(request.LastName, MaxShortLength) ?? string.Empty,
                Email = Cut(request.Email, MaxShortLength)!,
                Phone = Cut(request.Phone, 40) ?? string.Empty,
                Source = source,
                IdentifiedAt = VisitorTime.Normalize(_clock.GetUtcNow().UtcDateTime),
            },
        }, ct).ConfigureAwait(false);
        return written.IsSuccess ? Result.Success() : Map(written, "identify");
    }

    public async Task<Result> CountDropAsync(VisitorDropRequest request, CancellationToken ct)
    {
        if (request is null || !VisitorDropReasons.IsKnown(request.Reason)) return Result.Failure("Unknown drop reason.", BadRequest);
        var at = request.ReceivedAt == default ? _clock.GetUtcNow().UtcDateTime : request.ReceivedAt;
        var counted = await _repo.CountDropAsync(new VisitorDropCommand
        {
            Day = VisitorClock.DayKey(VisitorTime.Normalize(at)),
            Reason = request.Reason,
        }, ct).ConfigureAwait(false);
        if (counted.IsSuccess) return Result.Success();
        _logger.LogError("Visitor drop count failed: {Error}", counted.Error);
        return Result.Failure(counted.Error ?? "Drop count failed.", counted.ErrorCode == NotFound || counted.ErrorCode == BadRequest ? counted.ErrorCode : StoreFailed);
    }

    /// <summary>
    /// Scores the session as stored, raises the stored score when it grew, and marks a Florida
    /// session that reached the threshold, once. The mark is the whole of the intent signal: the
    /// daily report counts and names the sessions that carry it.
    /// </summary>
    internal async Task<Result> RescoreAsync(VisitorSession session, CancellationToken ct)
    {
        var score = VisitorIntentScoring.Score(session);
        if (score > session.Score)
        {
            var scored = await _repo.ScoreAsync(session.Id, new VisitorScoreCommand { Score = score }, ct).ConfigureAwait(false);
            if (!scored.IsSuccess) return Map(scored, "score");
            session = scored.Value!;
        }

        if (!session.Florida || !VisitorIntentScoring.ReachesIntent(session.Score) || session.IntentReachedAt is not null) return Result.Success();

        var marked = await _repo.MarkIntentReachedAsync(
            session.Id,
            new VisitorIntentReachedCommand { At = VisitorTime.Normalize(_clock.GetUtcNow().UtcDateTime) },
            ct).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            _logger.LogError("Visitor intent mark failed: {Error}", marked.Error);
            return Result.Failure(marked.Error ?? "Intent mark failed.", StoreFailed);
        }
        if (marked.Value)
            _logger.LogInformation("Visitor intent reached by a Florida session scoring {Score}.", session.Score);
        return Result.Success();
    }

    internal static bool IsFlorida(VisitorGeo? geo) =>
        string.Equals(geo?.Country?.Trim(), "US", StringComparison.OrdinalIgnoreCase)
        && string.Equals(geo?.Region?.Trim(), "FL", StringComparison.OrdinalIgnoreCase);

    private Result Map<T>(Result<T> result, string what)
    {
        if (result.Status == ResultStatus.NotFound) return Result.Failure(result.Error ?? "Not found.", NotFound);
        var refused = string.Equals(result.Error, "GeekRepository refused the request.", StringComparison.Ordinal);
        if (!refused) _logger.LogError("Visitor {What} failed: {Error}", what, result.Error);
        return Result.Failure(result.Error ?? "Visitor store failed.", refused ? BadRequest : StoreFailed);
    }

    private static VisitorSessionSnapshot? Trim(VisitorSessionSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        if (snapshot.Arrival is { } a)
        {
            a.LandingUrl = Cut(a.LandingUrl, MaxUrlLength);
            a.Referrer = Cut(a.Referrer, MaxUrlLength);
            if (a.Utm is { } u)
            {
                u.Source = Cut(u.Source, MaxShortLength); u.Medium = Cut(u.Medium, MaxShortLength); u.Campaign = Cut(u.Campaign, MaxShortLength);
                u.Term = Cut(u.Term, MaxShortLength); u.Content = Cut(u.Content, MaxShortLength);
            }
            if (a.ClickIds is { } c)
            {
                c.Gclid = Cut(c.Gclid, MaxShortLength); c.Gbraid = Cut(c.Gbraid, MaxShortLength); c.Wbraid = Cut(c.Wbraid, MaxShortLength);
                c.Fbclid = Cut(c.Fbclid, MaxShortLength); c.Msclkid = Cut(c.Msclkid, MaxShortLength); c.LiFatId = Cut(c.LiFatId, MaxShortLength); c.Ttclid = Cut(c.Ttclid, MaxShortLength);
            }
        }
        if (snapshot.Environment?.Browser is { } b)
        {
            b.Languages = (b.Languages ?? new List<string>()).Take(10).Select(l => Cut(l, 35)!).Where(l => l is not null).ToList();
            b.Timezone = Cut(b.Timezone, 64);
        }
        if (snapshot.Environment?.HighEntropy is { } h)
            h.FullVersionList = (h.FullVersionList ?? new List<string>()).Take(10).Select(v => Cut(v, 120)!).Where(v => v is not null).ToList();
        return snapshot;
    }

    private static string? Cut(string? value, int max)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        return v.Length <= max ? v : v[..max];
    }
}
