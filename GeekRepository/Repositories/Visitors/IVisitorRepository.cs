using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekRepository.Repositories.Visitors;

/// <summary>
/// The geek_visitors store. Every write lands once under the row's version token, every method
/// answers a Result, and nothing here throws past its own boundary: a database failure is a
/// Failure the caller reads, not an exception it catches.
/// </summary>
public interface IVisitorRepository
{
    // Sessions
    Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> PageEndAsync(Guid sessionId, VisitorPageEndCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> ClickAsync(Guid sessionId, VisitorClickCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> VitalsAsync(Guid sessionId, VisitorVitalsCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> SchedulerStepAsync(Guid sessionId, VisitorSchedulerStepCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> ContactSubmittedAsync(Guid sessionId, VisitorContactCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> BrowserGeoAsync(Guid sessionId, VisitorBrowserGeoCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> ScoreAsync(Guid sessionId, VisitorScoreCommand command, CancellationToken ct = default);
    Task<Result<VisitorSession>> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(
        DateTime fromUtc, DateTime toUtc, DateTime? afterStartedAt, long? afterSequence, int limit, CancellationToken ct = default);
    Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(
        Guid visitorId, DateTime sinceUtc, int limit, CancellationToken ct = default);

    // Intent, once per session: true when this call set it, false when it was already set
    Task<Result<bool>> MarkIntentReachedAsync(Guid sessionId, VisitorIntentReachedCommand command, CancellationToken ct = default);

    // Profiles
    Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand command, CancellationToken ct = default);
    Task<Result<VisitorProfile>> GetProfileAsync(Guid visitorId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    // Drops per New York day
    Task<Result> CountDropAsync(VisitorDropCommand command, CancellationToken ct = default);
    Task<Result<VisitorFilterDay>> GetFilterDayAsync(string day, CancellationToken ct = default);

    // Daily report claims
    Task<Result<bool>> ClaimDailyReportAsync(string date, VisitorDailyReportClaimCommand command, CancellationToken ct = default);
    Task<Result> MarkDailyReportBuiltAsync(string date, VisitorDailyReportBuiltCommand command, CancellationToken ct = default);
    Task<Result> ReleaseDailyReportAsync(string date, VisitorDailyReportReleaseCommand command, CancellationToken ct = default);
    Task<Result<VisitorDailyReport>> GetDailyReportAsync(string date, CancellationToken ct = default);

    // Retention
    Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand command, CancellationToken ct = default);
}
