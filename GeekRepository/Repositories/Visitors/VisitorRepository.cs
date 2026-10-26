using System.Globalization;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Visitors;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Visitors;

/// <summary>
/// geek_visitors in GeekRepository's own Postgres, through <see cref="VisitorsDbContext"/>.
/// Every write is one read-modify-write of the row under its version token: the row is loaded,
/// changed, its version bumped and saved, and a save that lost a race to another beacon is
/// retried against the fresh row. So a score only ever rises, a scheduler step keeps its first
/// time, a page or a click lands once under its id, and an intent mark or a report claim has
/// exactly one winner, whichever instance of GeekAPI sent it. Dates are UTC at millisecond
/// precision, so a claim's time compares equal to itself when it is released.
/// </summary>
public sealed class VisitorRepository : IVisitorRepository
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(180);
    public const int MaxPagesPerSession = 200;
    public const int MaxClicksPerPage = 50;
    public const int MaxAdditionalIps = 10;
    public const int PurgeBatch = 500;
    public const int Attempts = 8;

    private readonly VisitorsDbContext _db;
    private readonly ILogger<VisitorRepository> _logger;

    public VisitorRepository(VisitorsDbContext db, ILogger<VisitorRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ----- Sessions -----

    public async Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result<VisitorSession>.Failure("A page view is required.");
        if (command.SessionId == Guid.Empty || command.VisitorId == Guid.Empty)
            return Result<VisitorSession>.Failure("sessionId and visitorId are required.");
        if (command.Page is null || string.IsNullOrWhiteSpace(command.Page.PageViewId) || string.IsNullOrWhiteSpace(command.Page.Path))
            return Result<VisitorSession>.Failure("page.pageViewId and page.path are required.");

        var now = VisitorTime.Normalize(command.ReceivedAt);
        var page = command.Page;

        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            try
            {
                var row = await LoadSessionAsync(command.SessionId, track: true, ct).ConfigureAwait(false);
                if (row is null)
                {
                    var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == command.VisitorId, ct).ConfigureAwait(false);
                    if (visitor is null)
                    {
                        visitor = new VisitorRow { Id = command.VisitorId, FirstSeenAt = now, LastSeenAt = now, SessionCount = 1 };
                        _db.Visitors.Add(visitor);
                    }
                    else
                    {
                        visitor.SessionCount++;
                        if (now > visitor.LastSeenAt) visitor.LastSeenAt = now;
                        visitor.Version++;
                    }

                    row = VisitorRowMapping.NewSession(command, now, visitor.SessionCount);
                    row.Pages.Add(VisitorRowMapping.NewPage(row.Id, 0, page, now));
                    _db.Sessions.Add(row);
                }
                else
                {
                    var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == row.VisitorId, ct).ConfigureAwait(false);
                    if (visitor is not null && now > visitor.LastSeenAt)
                    {
                        visitor.LastSeenAt = now;
                        visitor.Version++;
                    }

                    if (now > row.LastSeenAt) row.LastSeenAt = now;
                    if (!string.IsNullOrEmpty(command.Ip)
                        && row.Ip != command.Ip
                        && !row.AdditionalIps.Contains(command.Ip)
                        && row.AdditionalIps.Count < MaxAdditionalIps)
                    {
                        row.AdditionalIps = new List<string>(row.AdditionalIps) { command.Ip };
                    }

                    if (row.Pages.All(p => p.PageViewId != page.PageViewId) && row.Pages.Count < MaxPagesPerSession)
                    {
                        var seq = row.Pages.Count == 0 ? 0 : row.Pages.Max(p => p.Seq) + 1;
                        row.Pages.Add(VisitorRowMapping.NewPage(row.Id, seq, page, now));
                    }
                    row.Version++;
                }

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<VisitorSession>.Success(VisitorRowMapping.ToRecord(row));
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < Attempts)
            {
                // Another beacon for the same session or visitor landed first: load it and try again.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Visitor page view write failed.");
                return Result<VisitorSession>.Failure("Visitor store write failed.");
            }
        }
    }

    public Task<Result<VisitorSession>> PageEndAsync(Guid sessionId, VisitorPageEndCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A page end is required."));
        if (string.IsNullOrWhiteSpace(command.PageViewId)) return Task.FromResult(Result<VisitorSession>.Failure("pageViewId is required."));
        var at = VisitorTime.Normalize(command.ReceivedAt);
        return WriteSessionAsync(sessionId, row =>
        {
            var page = row.Pages.FirstOrDefault(p => p.PageViewId == command.PageViewId);
            if (page is not null)
            {
                page.ActiveMs = Math.Max(page.ActiveMs, Math.Max(0, command.ActiveMs));
                page.MaxScrollPct = Math.Max(page.MaxScrollPct, Math.Clamp(command.MaxScrollPct, 0, 100));
            }
            if (at > row.LastSeenAt) row.LastSeenAt = at;
        }, "page end", ct);
    }

    public Task<Result<VisitorSession>> ClickAsync(Guid sessionId, VisitorClickCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A click is required."));
        if (string.IsNullOrWhiteSpace(command.PageViewId)) return Task.FromResult(Result<VisitorSession>.Failure("pageViewId is required."));
        var click = command.Click ?? new VisitorPageClick();
        var at = VisitorTime.Normalize(command.ReceivedAt);
        click.At = at;
        return WriteSessionAsync(sessionId, row =>
        {
            var page = row.Pages.FirstOrDefault(p => p.PageViewId == command.PageViewId);
            if (page is not null && page.Clicks.Count < MaxClicksPerPage)
            {
                var seq = page.Clicks.Count == 0 ? 0 : page.Clicks.Max(c => c.Seq) + 1;
                page.Clicks.Add(VisitorRowMapping.NewClick(row.Id, page.PageViewId, seq, click));
            }
            if (at > row.LastSeenAt) row.LastSeenAt = at;
        }, "click", ct);
    }

    public Task<Result<VisitorSession>> VitalsAsync(Guid sessionId, VisitorVitalsCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A vital is required."));
        if (string.IsNullOrWhiteSpace(command.PageViewId)) return Task.FromResult(Result<VisitorSession>.Failure("pageViewId is required."));
        var name = command.Name?.Trim().ToUpperInvariant();
        if (name is null || !VisitorVitalNames.All.Contains(name))
            return Task.FromResult(Result<VisitorSession>.Failure("name must be one of LCP, INP, CLS, FCP, TTFB."));
        if (double.IsNaN(command.Value) || double.IsInfinity(command.Value))
            return Task.FromResult(Result<VisitorSession>.Failure("value must be a number."));

        return WriteSessionAsync(sessionId, row =>
        {
            var page = row.Pages.FirstOrDefault(p => p.PageViewId == command.PageViewId);
            if (page is null) return;
            switch (name)
            {
                case "LCP": page.Lcp = command.Value; break;
                case "INP": page.Inp = command.Value; break;
                case "CLS": page.Cls = command.Value; break;
                case "FCP": page.Fcp = command.Value; break;
                case "TTFB": page.Ttfb = command.Value; break;
            }
        }, "vitals", ct);
    }

    public Task<Result<VisitorSession>> SchedulerStepAsync(Guid sessionId, VisitorSchedulerStepCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A scheduler step is required."));
        if (!VisitorSchedulerSteps.All.Contains(command.Step ?? string.Empty))
            return Task.FromResult(Result<VisitorSession>.Failure("step must be view, date_picked, slot_held or booked."));
        var at = VisitorTime.Normalize(command.At);
        return WriteSessionAsync(sessionId, row =>
        {
            switch (command.Step)
            {
                case VisitorSchedulerSteps.View: row.SchedulerViewedAt = Earliest(row.SchedulerViewedAt, at); break;
                case VisitorSchedulerSteps.DatePicked: row.SchedulerDatePickedAt = Earliest(row.SchedulerDatePickedAt, at); break;
                case VisitorSchedulerSteps.SlotHeld: row.SchedulerSlotHeldAt = Earliest(row.SchedulerSlotHeldAt, at); break;
                case VisitorSchedulerSteps.Booked: row.SchedulerBookedAt = Earliest(row.SchedulerBookedAt, at); break;
            }
            if (at > row.LastSeenAt) row.LastSeenAt = at;
        }, "scheduler step", ct);
    }

    public Task<Result<VisitorSession>> ContactSubmittedAsync(Guid sessionId, VisitorContactCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A contact submission is required."));
        var at = VisitorTime.Normalize(command.At);
        return WriteSessionAsync(sessionId, row =>
        {
            row.ContactSubmittedAt = Earliest(row.ContactSubmittedAt, at);
            if (at > row.LastSeenAt) row.LastSeenAt = at;
        }, "contact submitted", ct);
    }

    public Task<Result<VisitorSession>> BrowserGeoAsync(Guid sessionId, VisitorBrowserGeoCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A position is required."));
        var position = command.Position;
        if (position is null
            || double.IsNaN(position.Latitude) || double.IsNaN(position.Longitude)
            || position.Latitude < -90 || position.Latitude > 90
            || position.Longitude < -180 || position.Longitude > 180)
            return Task.FromResult(Result<VisitorSession>.Failure("position must carry a latitude and a longitude."));

        var at = VisitorTime.Normalize(position.At);
        return WriteSessionAsync(sessionId, row =>
        {
            row.BrowserGeoLatitude = position.Latitude;
            row.BrowserGeoLongitude = position.Longitude;
            row.BrowserGeoAccuracyMeters = position.AccuracyMeters;
            row.BrowserGeoAt = at;
            if (command.MarkFlorida) row.Florida = true;
            if (at > row.LastSeenAt) row.LastSeenAt = at;
        }, "browser geo", ct);
    }

    public Task<Result<VisitorSession>> ScoreAsync(Guid sessionId, VisitorScoreCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(Result<VisitorSession>.Failure("A score is required."));
        if (double.IsNaN(command.Score) || double.IsInfinity(command.Score))
            return Task.FromResult(Result<VisitorSession>.Failure("score must be a number."));
        var score = Math.Clamp(command.Score, 0, 100);
        return WriteSessionAsync(sessionId, row => row.Score = Math.Max(row.Score, score), "score", ct);
    }

    public async Task<Result<VisitorSession>> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty) return Result<VisitorSession>.Failure("sessionId is required.");
        try
        {
            var row = await LoadSessionAsync(sessionId, track: false, ct).ConfigureAwait(false);
            return row is null ? Result<VisitorSession>.NotFound("Session not found.") : Result<VisitorSession>.Success(VisitorRowMapping.ToRecord(row));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor session read failed.");
            return Result<VisitorSession>.Failure("Visitor store read failed.");
        }
    }

    public async Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(
        DateTime fromUtc, DateTime toUtc, DateTime? afterStartedAt, long? afterSequence, int limit, CancellationToken ct = default)
    {
        try
        {
            var from = VisitorTime.Normalize(fromUtc);
            var to = VisitorTime.Normalize(toUtc);
            var query = _db.Sessions.AsNoTracking()
                .Include(s => s.Pages).ThenInclude(p => p.Clicks)
                .Where(s => s.StartedAt >= from && s.StartedAt < to);
            if (afterStartedAt is not null && afterSequence is not null)
            {
                var after = VisitorTime.Normalize(afterStartedAt.Value);
                var afterRow = afterSequence.Value;
                query = query.Where(s => s.StartedAt > after || (s.StartedAt == after && s.RowNo > afterRow));
            }

            var rows = await query
                .OrderBy(s => s.StartedAt).ThenBy(s => s.RowNo)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<VisitorSession>>.Success(rows.Select(VisitorRowMapping.ToRecord).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor session range read failed.");
            return Result<IReadOnlyList<VisitorSession>>.Failure("Visitor store read failed.");
        }
    }

    public async Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(
        Guid visitorId, DateTime sinceUtc, int limit, CancellationToken ct = default)
    {
        if (visitorId == Guid.Empty) return Result<IReadOnlyList<VisitorSession>>.Failure("visitorId is required.");
        try
        {
            var since = VisitorTime.Normalize(sinceUtc);
            var rows = await _db.Sessions.AsNoTracking()
                .Include(s => s.Pages).ThenInclude(p => p.Clicks)
                .Where(s => s.VisitorId == visitorId && s.StartedAt >= since)
                .OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.RowNo)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<VisitorSession>>.Success(rows.Select(VisitorRowMapping.ToRecord).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor sessions-for-visitor read failed.");
            return Result<IReadOnlyList<VisitorSession>>.Failure("Visitor store read failed.");
        }
    }

    // ----- Intent -----

    public async Task<Result<bool>> MarkIntentReachedAsync(Guid sessionId, VisitorIntentReachedCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result<bool>.Failure("A time is required.");
        if (sessionId == Guid.Empty) return Result<bool>.Failure("sessionId is required.");
        var at = VisitorTime.Normalize(command.At);

        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            try
            {
                var row = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
                if (row is null) return Result<bool>.NotFound("Session not found.");
                if (row.IntentReachedAt is not null) return Result<bool>.Success(false);
                row.IntentReachedAt = at;
                row.Version++;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < Attempts)
            {
                // Lost the race to another writer of this session; the reload decides who marked it.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Visitor intent mark failed.");
                return Result<bool>.Failure("Visitor store write failed.");
            }
        }
    }

    // ----- Profiles -----

    public async Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result<VisitorProfile>.Failure("An identity is required.");
        if (command.VisitorId == Guid.Empty) return Result<VisitorProfile>.Failure("visitorId is required.");
        var identity = command.Identity;
        if (identity is null || string.IsNullOrWhiteSpace(identity.Email))
            return Result<VisitorProfile>.Failure("identity.email is required.");

        var identifiedAt = VisitorTime.Normalize(identity.IdentifiedAt == default ? DateTime.UtcNow : identity.IdentifiedAt);

        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            try
            {
                var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == command.VisitorId, ct).ConfigureAwait(false);
                if (visitor is null)
                {
                    visitor = new VisitorRow { Id = command.VisitorId, FirstSeenAt = identifiedAt, LastSeenAt = identifiedAt, SessionCount = 0 };
                    _db.Visitors.Add(visitor);
                }
                else
                {
                    if (identifiedAt > visitor.LastSeenAt) visitor.LastSeenAt = identifiedAt;
                    visitor.Version++;
                }
                visitor.IdentityFirstName = identity.FirstName ?? string.Empty;
                visitor.IdentityLastName = identity.LastName ?? string.Empty;
                visitor.IdentityEmail = identity.Email.Trim();
                visitor.IdentityPhone = identity.Phone ?? string.Empty;
                visitor.IdentitySource = identity.Source ?? string.Empty;
                visitor.IdentifiedAt = identifiedAt;

                if (command.SessionId is Guid sessionId && sessionId != Guid.Empty)
                {
                    var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
                    if (session is not null)
                    {
                        if (identity.Source == VisitorIdentitySources.Booking)
                            session.SchedulerBookedAt = Earliest(session.SchedulerBookedAt, identifiedAt);
                        else
                            session.ContactSubmittedAt = Earliest(session.ContactSubmittedAt, identifiedAt);
                        if (identifiedAt > session.LastSeenAt) session.LastSeenAt = identifiedAt;
                        session.Version++;
                    }
                }

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<VisitorProfile>.Success(VisitorRowMapping.ToRecord(visitor));
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < Attempts)
            {
                // A beacon for the same visitor or session landed first.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Visitor identify failed.");
                return Result<VisitorProfile>.Failure("Visitor store write failed.");
            }
        }
    }

    public async Task<Result<VisitorProfile>> GetProfileAsync(Guid visitorId, CancellationToken ct = default)
    {
        if (visitorId == Guid.Empty) return Result<VisitorProfile>.Failure("visitorId is required.");
        try
        {
            var row = await _db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitorId, ct).ConfigureAwait(false);
            return row is null ? Result<VisitorProfile>.NotFound("Visitor not found.") : Result<VisitorProfile>.Success(VisitorRowMapping.ToRecord(row));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor profile read failed.");
            return Result<VisitorProfile>.Failure("Visitor store read failed.");
        }
    }

    public async Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0) return Result<IReadOnlyList<VisitorProfile>>.Success(Array.Empty<VisitorProfile>());
        try
        {
            var wanted = ids.Distinct().Take(2000).ToList();
            var rows = await _db.Visitors.AsNoTracking().Where(v => wanted.Contains(v.Id)).ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<VisitorProfile>>.Success(rows.Select(VisitorRowMapping.ToRecord).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor profiles read failed.");
            return Result<IReadOnlyList<VisitorProfile>>.Failure("Visitor store read failed.");
        }
    }

    // ----- Drops -----

    public async Task<Result> CountDropAsync(VisitorDropCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result.Failure("A drop is required.", "bad_request");
        if (!VisitorDropReasons.IsKnown(command.Reason)) return Result.Failure("Unknown drop reason.", "bad_request");
        if (!IsDayKey(command.Day)) return Result.Failure("day must be yyyy-MM-dd.", "bad_request");

        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            try
            {
                var row = await _db.Drops.FirstOrDefaultAsync(d => d.Day == command.Day && d.Reason == command.Reason, ct).ConfigureAwait(false);
                if (row is null)
                {
                    _db.Drops.Add(new VisitorDropRow { Day = command.Day, Reason = command.Reason, DropCount = 1 });
                }
                else
                {
                    row.DropCount++;
                    row.Version++;
                }
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result.Success();
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < Attempts)
            {
                // Another drop of the same day and reason landed first; count on top of it.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Visitor drop count failed.");
                return Result.Failure("Visitor store write failed.");
            }
        }
    }

    public async Task<Result<VisitorFilterDay>> GetFilterDayAsync(string day, CancellationToken ct = default)
    {
        if (!IsDayKey(day)) return Result<VisitorFilterDay>.Failure("day must be yyyy-MM-dd.");
        try
        {
            var rows = await _db.Drops.AsNoTracking().Where(d => d.Day == day).ToListAsync(ct).ConfigureAwait(false);
            if (rows.Count == 0) return Result<VisitorFilterDay>.NotFound("No drops recorded for that day.");
            return Result<VisitorFilterDay>.Success(new VisitorFilterDay
            {
                Id = day,
                Counts = rows.ToDictionary(r => r.Reason, r => r.DropCount, StringComparer.Ordinal),
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor filter day read failed.");
            return Result<VisitorFilterDay>.Failure("Visitor store read failed.");
        }
    }

    // ----- Daily report claims -----

    public async Task<Result<bool>> ClaimDailyReportAsync(string date, VisitorDailyReportClaimCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result<bool>.Failure("A claim is required.");
        if (!IsDayKey(date)) return Result<bool>.Failure("date must be yyyy-MM-dd.");
        _db.ChangeTracker.Clear();
        try
        {
            if (await _db.DailyReports.AnyAsync(r => r.Day == date, ct).ConfigureAwait(false)) return Result<bool>.Success(false);
            _db.DailyReports.Add(new VisitorDailyReportRow { Day = date, ClaimedAt = VisitorTime.Normalize(command.ClaimedAt) });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            // The insert lost: whoever holds the row now claimed the day. Read, do not guess.
            _db.ChangeTracker.Clear();
            try
            {
                var taken = await _db.DailyReports.AnyAsync(r => r.Day == date, ct).ConfigureAwait(false);
                if (taken) return Result<bool>.Success(false);
                _logger.LogError(ex, "Visitor daily report claim failed.");
                return Result<bool>.Failure("Visitor store write failed.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception readEx)
            {
                _logger.LogError(readEx, "Visitor daily report claim failed.");
                return Result<bool>.Failure("Visitor store write failed.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor daily report claim failed.");
            return Result<bool>.Failure("Visitor store write failed.");
        }
    }

    public async Task<Result> MarkDailyReportBuiltAsync(string date, VisitorDailyReportBuiltCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result.Failure("A built mark is required.", "bad_request");
        if (!IsDayKey(date)) return Result.Failure("date must be yyyy-MM-dd.", "bad_request");
        _db.ChangeTracker.Clear();
        try
        {
            var row = await _db.DailyReports.FirstOrDefaultAsync(r => r.Day == date, ct).ConfigureAwait(false);
            if (row is null) return Result.Failure("Report claim not found.", "not_found");
            row.BuiltAt = VisitorTime.Normalize(command.BuiltAt);
            VisitorRowMapping.ApplyTotals(row, command.Totals ?? new VisitorReportTotals());
            VisitorRowMapping.ApplyPurge(row, command.Purge);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor daily report built mark failed.");
            return Result.Failure("Visitor store write failed.");
        }
    }

    public async Task<Result> ReleaseDailyReportAsync(string date, VisitorDailyReportReleaseCommand command, CancellationToken ct = default)
    {
        if (command is null) return Result.Failure("A release is required.", "bad_request");
        if (!IsDayKey(date)) return Result.Failure("date must be yyyy-MM-dd.", "bad_request");
        var claimedAt = VisitorTime.Normalize(command.ClaimedAt);
        _db.ChangeTracker.Clear();
        try
        {
            var row = await _db.DailyReports.FirstOrDefaultAsync(r => r.Day == date, ct).ConfigureAwait(false);
            if (row is null || row.BuiltAt is not null || VisitorTime.Normalize(row.ClaimedAt) != claimedAt) return Result.Success();
            _db.DailyReports.Remove(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row changed under us: someone else built or released it. Either way it is not ours to remove.
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor daily report release failed.");
            return Result.Failure("Visitor store write failed.");
        }
    }

    public async Task<Result<VisitorDailyReport>> GetDailyReportAsync(string date, CancellationToken ct = default)
    {
        if (!IsDayKey(date)) return Result<VisitorDailyReport>.Failure("date must be yyyy-MM-dd.");
        try
        {
            var row = await _db.DailyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Day == date, ct).ConfigureAwait(false);
            if (row is null) return Result<VisitorDailyReport>.NotFound("No report for that date.");
            var drops = await _db.Drops.AsNoTracking().Where(d => d.Day == date).ToListAsync(ct).ConfigureAwait(false);
            return Result<VisitorDailyReport>.Success(VisitorRowMapping.ToRecord(
                row,
                drops.ToDictionary(d => d.Reason, d => d.DropCount, StringComparer.Ordinal)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor daily report read failed.");
            return Result<VisitorDailyReport>.Failure("Visitor store read failed.");
        }
    }

    // ----- Retention -----

    public async Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand command, CancellationToken ct = default)
    {
        if (command is null || command.AsOf == default) return Result<VisitorPurgeResult>.Failure("asOf is required.");
        var cutoff = VisitorTime.Normalize(command.AsOf) - Retention;
        var result = new VisitorPurgeResult();
        try
        {
            while (true)
            {
                _db.ChangeTracker.Clear();
                var sessions = await _db.Sessions
                    .Include(s => s.Pages).ThenInclude(p => p.Clicks)
                    .Where(s => s.LastSeenAt < cutoff)
                    .OrderBy(s => s.LastSeenAt).ThenBy(s => s.RowNo)
                    .Take(PurgeBatch)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (sessions.Count == 0) break;
                _db.Sessions.RemoveRange(sessions);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                result.Sessions += sessions.Count;
            }

            while (true)
            {
                _db.ChangeTracker.Clear();
                var visitors = await _db.Visitors
                    .Where(v => v.LastSeenAt < cutoff)
                    .OrderBy(v => v.LastSeenAt)
                    .Take(PurgeBatch)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (visitors.Count == 0) break;
                _db.Visitors.RemoveRange(visitors);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                result.Visitors += visitors.Count;
            }

            return Result<VisitorPurgeResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visitor purge failed after {Sessions} sessions and {Visitors} visitors.", result.Sessions, result.Visitors);
            return Result<VisitorPurgeResult>.Failure("Visitor store purge failed.");
        }
    }

    // ----- Helpers -----

    private async Task<Result<VisitorSession>> WriteSessionAsync(
        Guid sessionId,
        Action<VisitorSessionRow> change,
        string what,
        CancellationToken ct)
    {
        if (sessionId == Guid.Empty) return Result<VisitorSession>.Failure("sessionId is required.");
        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            try
            {
                var row = await LoadSessionAsync(sessionId, track: true, ct).ConfigureAwait(false);
                if (row is null) return Result<VisitorSession>.NotFound("Session not found.");
                change(row);
                row.Version++;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result<VisitorSession>.Success(VisitorRowMapping.ToRecord(row));
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < Attempts)
            {
                // Another beacon for this session landed first; apply the change to the fresh row.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Visitor {What} write failed.", what);
                return Result<VisitorSession>.Failure("Visitor store write failed.");
            }
        }
    }

    private Task<VisitorSessionRow?> LoadSessionAsync(Guid sessionId, bool track, CancellationToken ct)
    {
        IQueryable<VisitorSessionRow> query = _db.Sessions.Include(s => s.Pages).ThenInclude(p => p.Clicks);
        if (!track) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    /// <summary>
    /// A write that lost a race: the version token no longer matched, or an insert found its key
    /// already there. The in-memory provider reports the second as an argument error, Postgres as
    /// a unique violation inside a DbUpdateException. Both are the same fact and are retried.
    /// </summary>
    private static bool IsRetryable(Exception ex) =>
        ex is DbUpdateException || ex is ArgumentException;

    private static DateTime? Earliest(DateTime? current, DateTime candidate) =>
        current is null || candidate < current.Value ? candidate : current;

    internal static bool IsDayKey(string? value) =>
        value is not null
        && value.Length == 10
        && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
