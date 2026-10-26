using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>GeekRepository's repo/visitors/* routes, as GeekAPI calls them.</summary>
public interface IVisitorRepositoryClient
{
    Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> PageEndAsync(Guid sessionId, VisitorPageEndCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> ClickAsync(Guid sessionId, VisitorClickCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> VitalsAsync(Guid sessionId, VisitorVitalsCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> SchedulerStepAsync(Guid sessionId, VisitorSchedulerStepCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> ContactSubmittedAsync(Guid sessionId, VisitorContactCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> BrowserGeoAsync(Guid sessionId, VisitorBrowserGeoCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> ScoreAsync(Guid sessionId, VisitorScoreCommand command, CancellationToken ct);
    Task<Result<VisitorSession>> GetSessionAsync(Guid sessionId, CancellationToken ct);
    Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(Guid visitorId, DateTime sinceUtc, CancellationToken ct);

    /// <summary>True when this call set the mark; false when the session already carried it.</summary>
    Task<Result<bool>> MarkIntentReachedAsync(Guid sessionId, VisitorIntentReachedCommand command, CancellationToken ct);

    Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand command, CancellationToken ct);
    Task<Result<VisitorProfile>> GetProfileAsync(Guid visitorId, CancellationToken ct);
    Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct);

    Task<Result> CountDropAsync(VisitorDropCommand command, CancellationToken ct);
    Task<Result<VisitorFilterDay>> GetFilterDayAsync(string day, CancellationToken ct);

    Task<Result<bool>> ClaimDailyReportAsync(string date, VisitorDailyReportClaimCommand command, CancellationToken ct);
    Task<Result> MarkDailyReportBuiltAsync(string date, VisitorDailyReportBuiltCommand command, CancellationToken ct);
    Task<Result> ReleaseDailyReportAsync(string date, VisitorDailyReportReleaseCommand command, CancellationToken ct);
    Task<Result<VisitorDailyReport>> GetDailyReportAsync(string date, CancellationToken ct);

    Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand command, CancellationToken ct);
}
