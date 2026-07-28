namespace GeekApplication.Models.ContentWriterV3;

public sealed record ResearchRunDto(
    Guid Id,
    Guid CampaignId,
    string Keyword,
    string Status,
    int DiscoveredSourceCount,
    decimal SpentBudget,
    decimal MaxBudget,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record CreateResearchRunCommand(
    Guid CampaignId,
    string Keyword,
    decimal MaxBudget);

public sealed record UpdateResearchRunStatusCommand(
    Guid Id,
    string Status,
    int DiscoveredSourceCount,
    decimal SpentBudget,
    string? ErrorMessage);

public sealed record ResearchSourceDto(
    Guid Id,
    Guid ResearchRunId,
    string SourceType,
    string? Url,
    string Title,
    string? Description,
    DateTime CreatedAtUtc);

public sealed record CreateResearchSourceCommand(
    Guid ResearchRunId,
    string SourceType,
    string? Url,
    string Title,
    string? Description);

public sealed record ResearchEvidenceDto(
    Guid Id,
    Guid ResearchSourceId,
    string Statement,
    string SupportLevel,
    bool ApprovedForClaim,
    int Confidence,
    DateTime CreatedAtUtc);

public sealed record CreateResearchEvidenceCommand(
    Guid ResearchSourceId,
    string Statement,
    string SupportLevel,
    int Confidence);

public sealed record ApproveEvidenceCommand(
    Guid Id);

public sealed record PainPointDto(
    Guid Id,
    Guid ClientId,
    string Name,
    string Description,
    string ReaderSymptom,
    string CostOfInaction,
    string OfferTerminology,
    List<string> Objections,
    int Confidence,
    DateTime? StaleSinceUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreatePainPointCommand(
    Guid ClientId,
    string Name,
    string Description,
    string ReaderSymptom,
    string CostOfInaction,
    string OfferTerminology,
    List<string> Objections,
    int Confidence);

public sealed record UpdatePainPointCommand(
    Guid Id,
    string Name,
    string Description,
    string ReaderSymptom,
    string CostOfInaction,
    string OfferTerminology,
    List<string> Objections,
    int Confidence);

public sealed record MarkPainPointStaleCommand(
    Guid Id);

public sealed record PainPointEvidenceLinkDto(
    Guid Id,
    Guid PainPointId,
    Guid ResearchEvidenceId,
    string Dimension,
    DateTime CreatedAtUtc);

public sealed record CreatePainPointEvidenceLinkCommand(
    Guid PainPointId,
    Guid ResearchEvidenceId,
    string Dimension);

public sealed record KeywordCandidateDto(
    Guid Id,
    Guid ClientId,
    string Keyword,
    int? SearchVolume,
    int? Difficulty,
    string? Intent,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateKeywordCandidateCommand(
    Guid ClientId,
    string Keyword,
    int? SearchVolume,
    int? Difficulty,
    string? Intent);

public sealed record UpdateKeywordCandidateStatusCommand(
    Guid Id,
    string Status);
