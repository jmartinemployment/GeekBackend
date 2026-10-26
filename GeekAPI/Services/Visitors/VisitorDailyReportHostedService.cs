using GeekAPI.HttpClients;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// Builds yesterday's visitor report once, at or after 07:00 New York time, runs the day's
/// retention purge, and records both on the day's row. Every minute it asks whether the previous
/// day is unclaimed; a claim is one insert keyed by the date, so two instances racing build it
/// once. Nothing ends in a log line alone: a compose that fails releases the claim and the next
/// tick tries again; a purge that fails is written to the row with its error, where the summary
/// route shows it; a row that cannot be written releases the claim so the next tick builds again;
/// and a claim that has had no build behind it for ten minutes, left by an instance that died or
/// lost its store between claiming and recording, is taken over by the next tick that sees it.
/// The report itself is composed on demand from the day's sessions
/// (<see cref="VisitorDailyReportComposer"/>, which api/visitors/internal/reports also serves);
/// what the row keeps is the day's totals, which carry no visitor data and outlive the sessions.
/// </summary>
public sealed class VisitorDailyReportHostedService : BackgroundService
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeOnly BuildAt = new(7, 0);
    /// <summary>A claim this old with no build behind it is abandoned; composing takes seconds.</summary>
    public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<VisitorDailyReportHostedService> _logger;

    public VisitorDailyReportHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<VisitorDailyReportHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval, _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (HostedServiceScan.ShouldLogAndContinue(ex, stoppingToken))
            {
                _logger.LogError(ex, "Visitor daily report tick failed.");
            }
        }
    }

    /// <summary>One look at the clock. True when this tick built and recorded the report.</summary>
    internal async Task<bool> TickAsync(CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var nowNewYork = VisitorClock.ToNewYork(nowUtc);
        if (TimeOnly.FromDateTime(nowNewYork) < BuildAt) return false;

        var day = DateOnly.FromDateTime(nowNewYork).AddDays(-1);
        var key = VisitorClock.DayKey(day);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IVisitorRepositoryClient>();
        var composer = scope.ServiceProvider.GetRequiredService<VisitorDailyReportComposer>();

        var existing = await repo.GetDailyReportAsync(key, ct).ConfigureAwait(false);
        if (existing.IsSuccess)
        {
            var held = existing.Value!;
            if (held.BuiltAt is not null) return false;
            if (nowUtc - held.ClaimedAt < StaleClaim) return false;

            var takeover = await repo.ReleaseDailyReportAsync(key, new VisitorDailyReportReleaseCommand { ClaimedAt = held.ClaimedAt }, ct).ConfigureAwait(false);
            if (!takeover.IsSuccess)
            {
                _logger.LogError("Visitor daily report {Date}: a claim from {ClaimedAt:u} has no build behind it and could not be released: {Error}", key, held.ClaimedAt, takeover.Error);
                return false;
            }
            _logger.LogWarning("Visitor daily report {Date}: a claim from {ClaimedAt:u} had no build behind it and was released; building now.", key, held.ClaimedAt);
        }
        else if (existing.Status != ResultStatus.NotFound)
        {
            _logger.LogError("Visitor daily report {Date}: could not read the claim: {Error}", key, existing.Error);
            return false;
        }

        var claimedAt = VisitorTime.Normalize(nowUtc);
        var claim = await repo.ClaimDailyReportAsync(key, new VisitorDailyReportClaimCommand { ClaimedAt = claimedAt }, ct).ConfigureAwait(false);
        if (!claim.IsSuccess)
        {
            _logger.LogError("Visitor daily report {Date}: claim failed: {Error}", key, claim.Error);
            return false;
        }
        if (!claim.Value) return false;

        // Claimed. From here the work runs to its end even if the host begins stopping, because a
        // claim left behind with no build would wait ten minutes for another tick to take it over.
        var content = await composer.ComposeAsync(day, CancellationToken.None).ConfigureAwait(false);
        if (!content.IsSuccess)
        {
            _logger.LogError("Visitor daily report {Date}: could not be composed: {Error}", key, content.Error);
            await repo.ReleaseDailyReportAsync(key, new VisitorDailyReportReleaseCommand { ClaimedAt = claimedAt }, CancellationToken.None).ConfigureAwait(false);
            return false;
        }
        var report = content.Value!;

        var purged = await repo.PurgeAsync(new VisitorPurgeCommand { AsOf = nowUtc }, CancellationToken.None).ConfigureAwait(false);
        var purge = new VisitorPurgeOutcome
        {
            At = VisitorTime.Normalize(_clock.GetUtcNow().UtcDateTime),
            Sessions = purged.IsSuccess ? purged.Value!.Sessions : null,
            Visitors = purged.IsSuccess ? purged.Value!.Visitors : null,
            Error = purged.IsSuccess ? null : purged.Error ?? "Purge failed.",
        };

        var recorded = await repo.MarkDailyReportBuiltAsync(
            key,
            new VisitorDailyReportBuiltCommand
            {
                BuiltAt = VisitorTime.Normalize(_clock.GetUtcNow().UtcDateTime),
                Totals = report.Totals,
                Purge = purge,
            },
            CancellationToken.None).ConfigureAwait(false);
        if (!recorded.IsSuccess)
        {
            // Composed, but the row does not say so, and the summary route answers 404 for the day
            // until it does. The claim is released so the next tick builds again; a release that
            // fails with the same outage leaves a claim the stale-claim rule takes over.
            _logger.LogError("Visitor daily report {Date}: built but the row could not be written, released for the next tick: {Error}", key, recorded.Error);
            await repo.ReleaseDailyReportAsync(key, new VisitorDailyReportReleaseCommand { ClaimedAt = claimedAt }, CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        _logger.LogInformation(
            "Visitor daily report {Date} built: {Visitors} visitors, {Sessions} sessions, {Identified} identified, {Intent} intent.",
            key, report.Totals.Visitors, report.Totals.Sessions, report.Totals.IdentifiedVisitors, report.Totals.IntentSessions);
        if (purge.Error is null)
            _logger.LogInformation("Visitor retention: {Sessions} sessions and {Visitors} visitors past 180 days removed.", purge.Sessions, purge.Visitors);
        else
            _logger.LogError("Visitor retention purge failed and is recorded on {Date}: {Error}", key, purge.Error);

        return true;
    }
}
