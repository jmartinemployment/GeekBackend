using GeekApplication.Models.ContentCreator;

namespace GeekApplication.Interfaces.ContentCreator;

public interface IGccCreateRepository
{
    Task<GccCreateDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GccCreateDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task<IReadOnlyList<GccCreateDto>> ListAsync(Guid? clientId, string? ownerUserId, CancellationToken ct = default);
    Task<GccCreateDto> CreateAsync(CreateGccCreateCommand command, CancellationToken ct = default);
    Task<GccCreateDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default);
    Task<GccCreateDto> UpdateBriefResearchAsync(Guid id, UpdateGccCreateBriefResearchCommand command, CancellationToken ct = default);

    /// <summary>
    /// Delete a create and everything beneath it: artifacts, their versions, and the approval
    /// events on those versions. Returns false when no such create exists.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IGccArtifactRepository
{
    Task<GccArtifactDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GccArtifactDto>> GetByCreateIdAsync(Guid createId, CancellationToken ct = default);
    Task<GccArtifactDto> CreateAsync(CreateGccArtifactCommand command, CancellationToken ct = default);
    Task<GccArtifactDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default);
}

public interface IGccArtifactVersionRepository
{
    Task<GccArtifactVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GccArtifactVersionDto>> GetByArtifactIdAsync(Guid artifactId, CancellationToken ct = default);
    Task<GccArtifactVersionDto> CreateAsync(CreateGccArtifactVersionCommand command, CancellationToken ct = default);
}

public interface IGccApprovalEventRepository
{
    Task<GccApprovalEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GccApprovalEventDto>> GetByArtifactVersionIdAsync(Guid artifactVersionId, CancellationToken ct = default);
    Task<GccApprovalEventDto> CreateAsync(CreateGccApprovalEventCommand command, CancellationToken ct = default);
}

public interface IGccClientRepository
{
    Task<GccClientDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<GccClientDto?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Every client, by name. This is the list the operator picks from.</summary>
    Task<IReadOnlyList<GccClientDto>> ListAsync(CancellationToken ct = default);

    Task<GccClientDto> CreateAsync(CreateGccClientCommand command, CancellationToken ct = default);

    /// <summary>Null when the client does not exist.</summary>
    Task<GccClientDto?> UpdateAsync(UpdateGccClientCommand command, CancellationToken ct = default);

    /// <summary>
    /// False when the client does not exist. A client with projects is refused by the database
    /// rather than deleted along with its record of work.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IGccSiteAnalysisRepository
{
    Task<GccSiteAnalysisDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Most recent (by CreatedAtUtc) site analysis for a normalized domain, or null if
    /// this domain has never been analyzed — the anchor for "has this domain been analyzed" /
    /// re-analyze checks, so a create's ProjectSiteRunId can point at one continuously-refreshed
    /// row per domain instead of accumulating orphaned duplicates.</summary>
    Task<GccSiteAnalysisDto?> GetLatestByDomainAsync(string domain, CancellationToken ct = default);
    /// <summary>True when any site analysis row is status <c>ready</c> (Workflow unlock gate).</summary>
    Task<bool> HasReadyAsync(CancellationToken ct = default);
    Task<GccSiteAnalysisDto> CreateAsync(CreateGccSiteAnalysisCommand command, CancellationToken ct = default);
    Task<GccSiteAnalysisDto?> UpdateAsync(Guid id, UpdateGccSiteAnalysisCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<GccSiteFindingDto>> ReplaceFindingsAsync(
        Guid analysisId,
        CreateGccSiteFindingsCommand command,
        CancellationToken ct = default);
    Task<IReadOnlyList<GccSiteFindingDto>> ListByAnalysisIdAsync(Guid analysisId, CancellationToken ct = default);
}

/// <summary>
/// Projects and their log.
/// </summary>
/// <remarks>
/// Every write here also writes the log entry for what it did, in the same transaction. There is no
/// method that changes a project without recording the change, and none that records a change
/// without making it.
///
/// Not-found is null, never an exception: the caller turns that into a 404, which is the only
/// thing it could mean.
/// </remarks>
public interface IGccProjectRepository
{
    Task<GccProjectDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Projects for one client, newest start date first.</summary>
    Task<IReadOnlyList<GccProjectDto>> ListByClientIdAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>
    /// Create, or return what this idempotency key already created.
    /// </summary>
    Task<GccProjectCreateResult> CreateAsync(CreateGccProjectCommand command, CancellationToken ct = default);

    /// <summary>Update the profile and schedule. Null when the project does not exist.</summary>
    Task<GccProjectDto?> UpdateAsync(UpdateGccProjectCommand command, CancellationToken ct = default);

    /// <summary>
    /// Move to a new status. Null when the project does not exist; the database refuses a finished
    /// status without its date, and a date on any other status.
    /// </summary>
    Task<GccProjectDto?> ChangeStatusAsync(ChangeGccProjectStatusCommand command, CancellationToken ct = default);

    /// <summary>The project's log, oldest first. Empty only when the project does not exist.</summary>
    Task<IReadOnlyList<GccProjectLogEntryDto>> ListLogAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Soft-delete: sets DeletedAtUtc and logs it, in one transaction. False when the project does
    /// not exist or is already deleted. Never a real DELETE — see DeletedAtUtc on GccProject.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Remove one log entry, for real — a genuine DELETE, not a soft one. False when the project or
    /// the entry does not exist, or the entry belongs to a different project. Records a
    /// log_entry_deleted entry for the deletion itself, in the same transaction, so the log still
    /// shows that a removal happened even though the removed row's content does not survive it.
    /// </summary>
    Task<bool> DeleteLogEntryAsync(
        Guid projectId,
        long logEntryId,
        string actorUserId,
        CancellationToken ct = default);
}

/// <summary>
/// Tasks and logged time under a project.
/// </summary>
/// <remarks>
/// Every write records a project log entry in the same transaction. Rate and currency on a time
/// entry come from the client row inside that transaction and are never accepted from a caller.
/// </remarks>
public interface IGccTaskRepository
{
    Task<IReadOnlyList<GccTaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Null when the project does not exist.</summary>
    Task<GccTaskDto?> CreateTaskAsync(CreateGccTaskCommand command, CancellationToken ct = default);

    /// <summary>Null when the task does not exist.</summary>
    Task<GccTaskDto?> UpdateTaskAsync(UpdateGccTaskCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<GccTimeEntryDto>> ListTimeByProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Log time. Carries its refusal as a reason rather than an exception — "that client has no
    /// rate" is something the operator has to read.
    /// </summary>
    Task<GccTimeEntryResult> LogTimeAsync(CreateGccTimeEntryCommand command, CancellationToken ct = default);

    /// <summary>Totals for a project, with billable money grouped per currency.</summary>
    Task<GccProjectTimeTotals> TotalsForProjectAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>
/// What each project has promised to hand over.
/// </summary>
/// <remarks>
/// A deliverable is the project-side record of a create. Creating one is refused rather than
/// thrown when the create belongs to another client or is already listed elsewhere — both are
/// sentences the operator has to read.
/// </remarks>
public interface IGccDeliverableRepository
{
    Task<IReadOnlyList<GccDeliverableDto>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<GccDeliverableResult> CreateAsync(CreateGccDeliverableCommand command, CancellationToken ct = default);

    /// <summary>Null when the deliverable does not exist.</summary>
    Task<GccDeliverableDto?> ChangeStatusAsync(ChangeGccDeliverableStatusCommand command, CancellationToken ct = default);
}
