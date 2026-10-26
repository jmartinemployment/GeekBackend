using GeekAPI.HttpClients;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// Reads one New York day out of GeekRepository and builds its report. Shared by the 07:00 send
/// and by the on-demand CSV route, so both describe the day the same way.
/// </summary>
public sealed class VisitorDailyReportComposer
{
    private readonly IVisitorRepositoryClient _repo;

    public VisitorDailyReportComposer(IVisitorRepositoryClient repo) => _repo = repo;

    public async Task<Result<VisitorDailyReportContent>> ComposeAsync(DateOnly day, CancellationToken ct)
    {
        var (start, end) = VisitorClock.DayWindowUtc(day);

        // One range read covers the day and the 30 days before it: the day's sessions are the
        // tail of it, and the visitor scores are summed over the whole of it.
        var recent = await _repo.ListSessionsAsync(start - VisitorDailyReportBuilder.VisitorScoreWindow, end, ct).ConfigureAwait(false);
        if (!recent.IsSuccess) return Result<VisitorDailyReportContent>.Failure(recent.Error ?? "Session read failed.");
        var recentSessions = recent.Value ?? Array.Empty<VisitorSession>();
        var daySessions = recentSessions.Where(s => s.StartedAt >= start && s.StartedAt < end).ToList();

        var visitorIds = daySessions.Select(s => s.VisitorId).Distinct().ToList();
        var profiles = await _repo.GetProfilesAsync(visitorIds, ct).ConfigureAwait(false);
        if (!profiles.IsSuccess) return Result<VisitorDailyReportContent>.Failure(profiles.Error ?? "Profile read failed.");

        var key = VisitorClock.DayKey(day);
        var drops = await _repo.GetFilterDayAsync(key, ct).ConfigureAwait(false);
        if (!drops.IsSuccess && drops.Status != ResultStatus.NotFound)
            return Result<VisitorDailyReportContent>.Failure(drops.Error ?? "Drop counts read failed.");

        var content = VisitorDailyReportBuilder.Build(
            day,
            daySessions,
            profiles.Value ?? Array.Empty<VisitorProfile>(),
            drops.IsSuccess ? drops.Value : null,
            VisitorDailyReportBuilder.VisitorScores(recentSessions));
        return Result<VisitorDailyReportContent>.Success(content);
    }
}
