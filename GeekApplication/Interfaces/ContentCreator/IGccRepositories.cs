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
    Task<GccClientDto> CreateAsync(CreateGccClientCommand command, CancellationToken ct = default);
}

public interface IGccSiteAnalysisRepository
{
    Task<GccSiteAnalysisDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Most recent (by CreatedAtUtc) site analysis for a normalized domain, or null if
    /// this domain has never been analyzed — the anchor for "has this domain been analyzed" /
    /// re-analyze checks, so a create's SiteAnalysisId can point at one continuously-refreshed
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
