using GeekApplication.Models.Crm;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Crm;

/// <summary>
/// What a person at the keyboard does in Geek-CRM, beside <see cref="ICrmStore"/> (which the receptionist and the importer
/// use): search people and companies, add and edit them, work a deal through the pipeline, keep notes and tasks, and read the
/// numbers for the home screen. GeekRepository implements it over its own Postgres and GeekAPI calls it over HTTP
/// (repo/crm/*). Every call is scoped to a tenant and runs in one transaction; failures carry a <see cref="CrmFailureCodes"/> value.
/// </summary>
public interface ICrmWorkStore
{
    /// <summary>The stages of the tenant's default pipeline, in order.</summary>
    Task<Result<IReadOnlyList<CrmStage>>> ListStagesAsync(Guid tenantId, CancellationToken ct = default);

    Task<Result<CrmDashboard>> GetDashboardAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// People and companies, newest activity first. <c>search</c> matches the name, an email address or a phone number
    /// (digits only are enough for a phone). Archived records are left out.
    /// </summary>
    Task<Result<IReadOnlyList<CrmParticipantSummary>>> SearchParticipantsAsync(Guid tenantId, string? search, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default);

    Task<Result<CrmParticipant>> CreateParticipantAsync(CreateParticipantCommand command, CancellationToken ct = default);

    Task<Result<CrmParticipant>> UpdateParticipantAsync(Guid participantId, UpdateParticipantCommand command, CancellationToken ct = default);

    /// <summary>
    /// Takes a person or company out of every list. Nothing is deleted: its deals, notes and holdings keep pointing at it,
    /// and a repeat call changes nothing.
    /// </summary>
    Task<Result<bool>> ArchiveParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmDealView>>> ListDealViewsAsync(Guid tenantId, bool openOnly, string? search, int limit, CancellationToken ct = default);

    Task<Result<CrmDealView>> GetDealViewAsync(Guid tenantId, Guid dealId, CancellationToken ct = default);

    Task<Result<CrmDealView>> UpdateDealAsync(Guid dealId, UpdateDealCommand command, CancellationToken ct = default);

    /// <summary>
    /// Activities of one person or deal, or every one when neither is named, newest first. <c>openTasks</c> lists the tasks not yet
    /// completed, soonest due first (those with no due date last).
    /// </summary>
    Task<Result<IReadOnlyList<CrmActivityView>>> ListActivityViewsAsync(Guid tenantId, Guid? participantId, Guid? dealId, string? channel, bool openTasks, int limit, CancellationToken ct = default);

    Task<Result<CrmActivity>> CreateActivityAsync(CreateActivityCommand command, CancellationToken ct = default);

    /// <summary>Marks a task done. A repeat call keeps the first completion time. Anything that is not a task is refused.</summary>
    Task<Result<CrmActivity>> CompleteTaskAsync(Guid tenantId, Guid activityId, CancellationToken ct = default);
}
