using GeekApplication.Models.ContentWriterV3;

namespace GeekApplication.Interfaces.ContentWriterV3;

public interface IContentCampaignRepository
{
    Task<ContentCampaignDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ContentCampaignDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task<ContentCampaignDto> CreateAsync(CreateContentCampaignCommand command, CancellationToken ct = default);
    Task<ContentCampaignDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IContentAssetRepository
{
    Task<ContentAssetDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ContentAssetDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default);
    Task<ContentAssetDto> CreateAsync(CreateContentAssetCommand command, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IContentAssetVersionRepository
{
    Task<ContentAssetVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ContentAssetVersionDto>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default);
    Task<ContentAssetVersionDto> CreateAsync(CreateContentAssetVersionCommand command, CancellationToken ct = default);
    Task<ContentAssetVersionDto> UpdateAsync(UpdateContentAssetVersionCommand command, CancellationToken ct = default);
}

public interface IStrategyBriefRepository
{
    Task<StrategyBriefDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<StrategyBriefDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default);
    Task<StrategyBriefDto> CreateAsync(CreateStrategyBriefCommand command, CancellationToken ct = default);
    Task<StrategyBriefDto> UpdateAsync(UpdateStrategyBriefCommand command, CancellationToken ct = default);
    Task<StrategyBriefDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<StrategyBriefDto> RejectAsync(Guid id, CancellationToken ct = default);
}

public interface IPublicationRepository
{
    Task<PublicationDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PublicationDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default);
    Task<PublicationDto> CreateAsync(CreatePublicationCommand command, CancellationToken ct = default);
    Task<PublicationDto> UpdateStatusAsync(UpdatePublicationStatusCommand command, CancellationToken ct = default);
}

public interface IPainPointRepository
{
    Task<PainPointDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PainPointDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task<PainPointDto> CreateAsync(CreatePainPointCommand command, CancellationToken ct = default);
    Task<PainPointDto> UpdateAsync(UpdatePainPointCommand command, CancellationToken ct = default);
    Task<PainPointDto> MarkStaleAsync(Guid id, CancellationToken ct = default);
}

public interface IResearchRunRepository
{
    Task<ResearchRunDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchRunDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default);
    Task<ResearchRunDto> CreateAsync(CreateResearchRunCommand command, CancellationToken ct = default);
    Task<ResearchRunDto> UpdateStatusAsync(UpdateResearchRunStatusCommand command, CancellationToken ct = default);
}

public interface IResearchSourceRepository
{
    Task<ResearchSourceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchSourceDto>> GetByResearchRunIdAsync(Guid researchRunId, CancellationToken ct = default);
    Task<ResearchSourceDto> CreateAsync(CreateResearchSourceCommand command, CancellationToken ct = default);
}

public interface IResearchEvidenceRepository
{
    Task<ResearchEvidenceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchEvidenceDto>> GetBySourceIdAsync(Guid sourceId, CancellationToken ct = default);
    Task<ResearchEvidenceDto> CreateAsync(CreateResearchEvidenceCommand command, CancellationToken ct = default);
    Task<ResearchEvidenceDto> ApproveAsync(Guid id, CancellationToken ct = default);
}

public interface IWorkspaceRepository
{
    Task<WorkspaceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkspaceDto> CreateAsync(CreateWorkspaceCommand command, CancellationToken ct = default);
    Task<WorkspaceDto> UpdateAsync(UpdateWorkspaceCommand command, CancellationToken ct = default);
}

public interface IClientRepository
{
    Task<ClientDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientDto>> GetByWorkspaceIdAsync(Guid workspaceId, CancellationToken ct = default);
    Task<ClientDto> CreateAsync(CreateClientCommand command, CancellationToken ct = default);
}

public interface IPainPointEvidenceLinkRepository
{
    Task<PainPointEvidenceLinkDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PainPointEvidenceLinkDto>> GetByPainPointIdAsync(Guid painPointId, CancellationToken ct = default);
    Task<PainPointEvidenceLinkDto> CreateAsync(CreatePainPointEvidenceLinkCommand command, CancellationToken ct = default);
}

public interface IReconciliationProposalRepository
{
    Task<ReconciliationProposalDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationProposalDto>> GetByResearchRunIdAsync(Guid researchRunId, CancellationToken ct = default);
    Task<ReconciliationProposalDto> CreateAsync(CreateReconciliationProposalCommand command, CancellationToken ct = default);
    Task<ReconciliationProposalDto> ApproveAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<ReconciliationProposalDto> DismissAsync(Guid id, Guid userId, CancellationToken ct = default);
}

public interface IKeywordCandidateRepository
{
    Task<KeywordCandidateDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<KeywordCandidateDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task<KeywordCandidateDto> CreateAsync(CreateKeywordCandidateCommand command, CancellationToken ct = default);
    Task<KeywordCandidateDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default);
}

public interface IReviewCommentRepository
{
    Task<ReviewCommentDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewCommentDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default);
    Task<ReviewCommentDto> CreateAsync(CreateReviewCommentCommand command, CancellationToken ct = default);
    Task<ReviewCommentDto> ResolveAsync(ResolveReviewCommentCommand command, CancellationToken ct = default);
}

public interface IApprovalEventRepository
{
    Task<ApprovalEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalEventDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default);
    Task<ApprovalEventDto> CreateAsync(CreateApprovalEventCommand command, CancellationToken ct = default);
}

public interface IPublicationEventRepository
{
    Task<PublicationEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PublicationEventDto>> GetByPublicationIdAsync(Guid publicationId, CancellationToken ct = default);
    Task<PublicationEventDto> CreateAsync(CreatePublicationEventCommand command, CancellationToken ct = default);
}

public interface IClientProfileRepository
{
    Task<ClientProfileDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ClientProfileDto?> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task<ClientProfileDto> CreateAsync(CreateClientProfileCommand command, CancellationToken ct = default);
}

public interface IClientProfileVersionRepository
{
    Task<ClientProfileVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientProfileVersionDto>> GetByProfileIdAsync(Guid profileId, CancellationToken ct = default);
    Task<ClientProfileVersionDto> CreateAsync(CreateClientProfileVersionCommand command, CancellationToken ct = default);
}

public interface IClientBrandVoiceLinkRepository
{
    Task<ClientBrandVoiceLinkDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientBrandVoiceLinkDto>> GetByProfileVersionIdAsync(Guid profileVersionId, CancellationToken ct = default);
    Task<ClientBrandVoiceLinkDto> CreateAsync(CreateClientBrandVoiceLinkCommand command, CancellationToken ct = default);
}
