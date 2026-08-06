namespace GeekApplication.Models.ContentCreator;

public sealed record GccCreateDto(
    Guid Id,
    Guid ClientId,
    Guid OwnerUserId,
    string StartingContentType,
    string Topic,
    string? Notes,
    Guid? SiteAnalysisId,
    string? SiteSectionJson,
    string? BriefJson,
    string? ResearchJson,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Department = "marketing");

public sealed record CreateGccCreateCommand(
    Guid ClientId,
    Guid OwnerUserId,
    string StartingContentType,
    string Topic,
    string? Notes = null,
    Guid? SiteAnalysisId = null,
    string? SiteSectionJson = null,
    string? BriefJson = null,
    string? ResearchJson = null,
    string Department = "marketing");

public sealed record UpdateGccCreateBriefResearchCommand(
    string? BriefJson,
    string? ResearchJson);

public sealed record GccArtifactDto(
    Guid Id,
    Guid CreateId,
    Guid? ParentArtifactId,
    string Type,
    string Name,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccArtifactCommand(
    Guid CreateId,
    string Type,
    string Name,
    Guid? ParentArtifactId = null);

public sealed record UpdateGccArtifactStatusCommand(
    Guid Id,
    string Status);

public sealed record GccArtifactVersionDto(
    Guid Id,
    Guid ArtifactId,
    int VersionNumber,
    string BodyDocumentJson,
    string? MetadataJson,
    uint RowVersion,
    DateTime CreatedAtUtc);

public sealed record CreateGccArtifactVersionCommand(
    Guid ArtifactId,
    string BodyDocumentJson,
    string? MetadataJson = null);

public sealed record GccApprovalEventDto(
    Guid Id,
    Guid ArtifactVersionId,
    Guid UserId,
    string Action,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record CreateGccApprovalEventCommand(
    Guid ArtifactVersionId,
    Guid UserId,
    string Action,
    string? Notes = null);

public sealed record GccSiteAnalysisDto(
    Guid Id,
    string Domain,
    string? SeedTopic,
    string GapsJson,
    string Status,
    Guid? SeoProjectId,
    Guid? SeoProfileId,
    string? ErrorMessage,
    string SiteModelJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccSiteAnalysisCommand(
    Guid? Id,
    string Domain,
    string? SeedTopic,
    string GapsJson,
    string Status = "ready",
    Guid? SeoProjectId = null,
    Guid? SeoProfileId = null,
    string? ErrorMessage = null,
    string? SiteModelJson = null);

public sealed record UpdateGccSiteAnalysisCommand(
    string Status,
    Guid? SeoProjectId,
    Guid? SeoProfileId,
    string? ErrorMessage,
    string? GapsJson,
    string? SiteModelJson);

public sealed record GccSiteFindingDto(
    Guid Id,
    Guid SiteAnalysisId,
    string FindingType,
    string Severity,
    string? AffectedUrl,
    string Title,
    string Summary,
    string DetailsJson,
    DateTime CreatedAtUtc);

public sealed record CreateGccSiteFindingsCommand(
    IReadOnlyList<GccSiteFindingDto> Findings);
