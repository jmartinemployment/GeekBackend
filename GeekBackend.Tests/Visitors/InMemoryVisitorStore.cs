using GeekAPI.HttpClients;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekBackend.Tests.Visitors;

/// <summary>
/// The repository client as a dictionary with the same once-only semantics as the store: one
/// winner per session for the intent mark, one winner per date for the daily report, a release
/// only by the holder of the claim. Used by the processor and hosted-service tests, which test the
/// orchestration and not the store; VisitorRepositoryTests covers the real repository.
/// </summary>
internal sealed class InMemoryVisitorStore : IVisitorRepositoryClient
{
    private readonly object _gate = new();
    public Dictionary<Guid, VisitorSession> Sessions { get; } = new();
    public Dictionary<Guid, VisitorProfile> Profiles { get; } = new();
    public Dictionary<string, VisitorFilterDay> FilterDays { get; } = new();
    public Dictionary<string, VisitorDailyReport> DailyReports { get; } = new();
    public bool FailWrites { get; set; }
    public bool FailReads { get; set; }
    public bool FailIntentMarks { get; set; }
    public bool FailBuiltMarks { get; set; }
    public bool FailPurges { get; set; }
    public int IntentMarkAttempts;
    public int ReleaseCalls;
    public int PurgeCalls;
    public TimeSpan ComposeDelay { get; set; } = TimeSpan.Zero;

    public Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand c, CancellationToken ct)
    {
        if (FailWrites) return Task.FromResult(Result<VisitorSession>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            if (!Sessions.TryGetValue(c.SessionId, out var s))
            {
                var profile = Profiles.TryGetValue(c.VisitorId, out var p) ? p : Profiles[c.VisitorId] = new VisitorProfile { Id = c.VisitorId, FirstSeenAt = c.ReceivedAt };
                profile.SessionCount++;
                profile.LastSeenAt = c.ReceivedAt;
                s = Sessions[c.SessionId] = new VisitorSession
                {
                    Id = c.SessionId, Sequence = Sessions.Count + 1, VisitorId = c.VisitorId, VisitNumber = profile.SessionCount, StartedAt = c.ReceivedAt, LastSeenAt = c.ReceivedAt,
                    Ip = c.Ip, Geo = c.Geo, EdgeRegion = c.EdgeRegion, Florida = c.Florida, UserAgent = c.UserAgent, ClientHints = c.ClientHints,
                    AcceptLanguage = c.AcceptLanguage, Dnt = c.Dnt, Environment = c.Snapshot?.Environment, Arrival = c.Snapshot?.Arrival,
                };
            }
            else if (s.Ip != c.Ip && !s.AdditionalIps.Contains(c.Ip) && s.AdditionalIps.Count < 10)
            {
                s.AdditionalIps.Add(c.Ip);
            }
            s.LastSeenAt = c.ReceivedAt > s.LastSeenAt ? c.ReceivedAt : s.LastSeenAt;
            if (s.Pages.All(p => p.PageViewId != c.Page.PageViewId) && s.Pages.Count < 200) s.Pages.Add(c.Page);
            return Task.FromResult(Result<VisitorSession>.Success(Copy(s)));
        }
    }

    public Task<Result<VisitorSession>> PageEndAsync(Guid id, VisitorPageEndCommand c, CancellationToken ct) => Mutate(id, s =>
    {
        var p = s.Pages.FirstOrDefault(x => x.PageViewId == c.PageViewId);
        if (p is null) return;
        p.ActiveMs = Math.Max(p.ActiveMs, c.ActiveMs);
        p.MaxScrollPct = Math.Max(p.MaxScrollPct, c.MaxScrollPct);
    });

    public Task<Result<VisitorSession>> ClickAsync(Guid id, VisitorClickCommand c, CancellationToken ct) => Mutate(id, s =>
    {
        var p = s.Pages.FirstOrDefault(x => x.PageViewId == c.PageViewId);
        if (p is not null && p.Clicks.Count < 50) p.Clicks.Add(c.Click);
    });

    public Task<Result<VisitorSession>> VitalsAsync(Guid id, VisitorVitalsCommand c, CancellationToken ct) => Mutate(id, s =>
    {
        var p = s.Pages.FirstOrDefault(x => x.PageViewId == c.PageViewId);
        if (p is null) return;
        p.Vitals ??= new VisitorWebVitals();
        switch (c.Name) { case "LCP": p.Vitals.Lcp = c.Value; break; case "INP": p.Vitals.Inp = c.Value; break; case "CLS": p.Vitals.Cls = c.Value; break; case "FCP": p.Vitals.Fcp = c.Value; break; case "TTFB": p.Vitals.Ttfb = c.Value; break; }
    });

    public Task<Result<VisitorSession>> SchedulerStepAsync(Guid id, VisitorSchedulerStepCommand c, CancellationToken ct) => Mutate(id, s =>
    {
        s.Scheduler ??= new VisitorSchedulerFunnel();
        switch (c.Step)
        {
            case VisitorSchedulerSteps.View: s.Scheduler.ViewedAt ??= c.At; break;
            case VisitorSchedulerSteps.DatePicked: s.Scheduler.DatePickedAt ??= c.At; break;
            case VisitorSchedulerSteps.SlotHeld: s.Scheduler.SlotHeldAt ??= c.At; break;
            case VisitorSchedulerSteps.Booked: s.Scheduler.BookedAt ??= c.At; break;
        }
    });

    public Task<Result<VisitorSession>> ContactSubmittedAsync(Guid id, VisitorContactCommand c, CancellationToken ct) => Mutate(id, s => s.ContactSubmittedAt ??= c.At);

    public Task<Result<VisitorSession>> BrowserGeoAsync(Guid id, VisitorBrowserGeoCommand c, CancellationToken ct) => Mutate(id, s =>
    {
        s.BrowserGeo = c.Position;
        if (c.MarkFlorida) s.Florida = true;
    });

    public Task<Result<VisitorSession>> ScoreAsync(Guid id, VisitorScoreCommand c, CancellationToken ct) => Mutate(id, s => s.Score = Math.Max(s.Score, c.Score));

    public Task<Result<VisitorSession>> GetSessionAsync(Guid id, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<VisitorSession>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            return Task.FromResult(Sessions.TryGetValue(id, out var s) ? Result<VisitorSession>.Success(Copy(s)) : Result<VisitorSession>.NotFound("Session not found."));
        }
    }

    public async Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (ComposeDelay > TimeSpan.Zero) await Task.Delay(ComposeDelay, ct);
        if (FailReads) return Result<IReadOnlyList<VisitorSession>>.Failure("GeekRepository answered 503.");
        lock (_gate)
        {
            IReadOnlyList<VisitorSession> rows = Sessions.Values.Where(s => s.StartedAt >= fromUtc && s.StartedAt < toUtc).OrderBy(s => s.StartedAt).Select(Copy).ToList();
            return Result<IReadOnlyList<VisitorSession>>.Success(rows);
        }
    }

    public Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(Guid visitorId, DateTime sinceUtc, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<IReadOnlyList<VisitorSession>>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            IReadOnlyList<VisitorSession> rows = Sessions.Values.Where(s => s.VisitorId == visitorId && s.StartedAt >= sinceUtc).Select(Copy).ToList();
            return Task.FromResult(Result<IReadOnlyList<VisitorSession>>.Success(rows));
        }
    }

    public Task<Result<bool>> MarkIntentReachedAsync(Guid id, VisitorIntentReachedCommand c, CancellationToken ct)
    {
        Interlocked.Increment(ref IntentMarkAttempts);
        if (FailIntentMarks) return Task.FromResult(Result<bool>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            if (!Sessions.TryGetValue(id, out var s)) return Task.FromResult(Result<bool>.NotFound("Session not found."));
            if (s.IntentReachedAt is not null) return Task.FromResult(Result<bool>.Success(false));
            s.IntentReachedAt = c.At;
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    public Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand c, CancellationToken ct)
    {
        if (FailWrites) return Task.FromResult(Result<VisitorProfile>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            var p = Profiles.TryGetValue(c.VisitorId, out var existing) ? existing : Profiles[c.VisitorId] = new VisitorProfile { Id = c.VisitorId, FirstSeenAt = c.Identity.IdentifiedAt };
            p.Identity = c.Identity;
            if (c.SessionId is { } sid && Sessions.TryGetValue(sid, out var s))
            {
                if (c.Identity.Source == VisitorIdentitySources.Booking) { s.Scheduler ??= new VisitorSchedulerFunnel(); s.Scheduler.BookedAt ??= c.Identity.IdentifiedAt; }
                else s.ContactSubmittedAt ??= c.Identity.IdentifiedAt;
            }
            return Task.FromResult(Result<VisitorProfile>.Success(p));
        }
    }

    public Task<Result<VisitorProfile>> GetProfileAsync(Guid visitorId, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<VisitorProfile>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            return Task.FromResult(Profiles.TryGetValue(visitorId, out var p) ? Result<VisitorProfile>.Success(p) : Result<VisitorProfile>.NotFound("Visitor not found."));
        }
    }

    public Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<IReadOnlyList<VisitorProfile>>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            IReadOnlyList<VisitorProfile> rows = ids.Where(Profiles.ContainsKey).Select(id => Profiles[id]).ToList();
            return Task.FromResult(Result<IReadOnlyList<VisitorProfile>>.Success(rows));
        }
    }

    public Task<Result> CountDropAsync(VisitorDropCommand c, CancellationToken ct)
    {
        if (FailWrites) return Task.FromResult(Result.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            var day = FilterDays.TryGetValue(c.Day, out var d) ? d : FilterDays[c.Day] = new VisitorFilterDay { Id = c.Day };
            day.Counts[c.Reason] = day.Counts.TryGetValue(c.Reason, out var n) ? n + 1 : 1;
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result<VisitorFilterDay>> GetFilterDayAsync(string day, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<VisitorFilterDay>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            return Task.FromResult(FilterDays.TryGetValue(day, out var d) ? Result<VisitorFilterDay>.Success(d) : Result<VisitorFilterDay>.NotFound("none"));
        }
    }

    public Task<Result<bool>> ClaimDailyReportAsync(string date, VisitorDailyReportClaimCommand c, CancellationToken ct)
    {
        if (FailWrites) return Task.FromResult(Result<bool>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            if (DailyReports.ContainsKey(date)) return Task.FromResult(Result<bool>.Success(false));
            DailyReports[date] = new VisitorDailyReport { Id = date, ClaimedAt = c.ClaimedAt };
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    public Task<Result> MarkDailyReportBuiltAsync(string date, VisitorDailyReportBuiltCommand c, CancellationToken ct)
    {
        if (FailBuiltMarks) return Task.FromResult(Result.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            if (!DailyReports.TryGetValue(date, out var r)) return Task.FromResult(Result.Failure("Report claim not found.", "not_found"));
            r.BuiltAt = c.BuiltAt; r.Totals = c.Totals; r.Purge = c.Purge;
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result> ReleaseDailyReportAsync(string date, VisitorDailyReportReleaseCommand c, CancellationToken ct)
    {
        Interlocked.Increment(ref ReleaseCalls);
        lock (_gate)
        {
            if (DailyReports.TryGetValue(date, out var r) && r.ClaimedAt == c.ClaimedAt && r.BuiltAt is null) DailyReports.Remove(date);
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result<VisitorDailyReport>> GetDailyReportAsync(string date, CancellationToken ct)
    {
        if (FailReads) return Task.FromResult(Result<VisitorDailyReport>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            return Task.FromResult(DailyReports.TryGetValue(date, out var r) ? Result<VisitorDailyReport>.Success(r) : Result<VisitorDailyReport>.NotFound("none"));
        }
    }

    public Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand c, CancellationToken ct)
    {
        Interlocked.Increment(ref PurgeCalls);
        if (FailWrites || FailPurges) return Task.FromResult(Result<VisitorPurgeResult>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            var cutoff = c.AsOf - TimeSpan.FromDays(180);
            var sessions = Sessions.Values.Where(s => s.LastSeenAt < cutoff).Select(s => s.Id).ToList();
            foreach (var id in sessions) Sessions.Remove(id);
            var visitors = Profiles.Values.Where(p => p.LastSeenAt < cutoff).Select(p => p.Id).ToList();
            foreach (var id in visitors) Profiles.Remove(id);
            return Task.FromResult(Result<VisitorPurgeResult>.Success(new VisitorPurgeResult { Sessions = sessions.Count, Visitors = visitors.Count }));
        }
    }

    private Task<Result<VisitorSession>> Mutate(Guid id, Action<VisitorSession> change)
    {
        if (FailWrites) return Task.FromResult(Result<VisitorSession>.Failure("GeekRepository answered 503."));
        lock (_gate)
        {
            if (!Sessions.TryGetValue(id, out var s)) return Task.FromResult(Result<VisitorSession>.NotFound("Session not found."));
            change(s);
            return Task.FromResult(Result<VisitorSession>.Success(Copy(s)));
        }
    }

    /// <summary>A detached copy, as a record read back from the store would be.</summary>
    private static VisitorSession Copy(VisitorSession s)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        return System.Text.Json.JsonSerializer.Deserialize<VisitorSession>(json)!;
    }
}
