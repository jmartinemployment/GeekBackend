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
}
