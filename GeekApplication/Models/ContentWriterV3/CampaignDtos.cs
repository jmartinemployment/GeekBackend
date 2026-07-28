namespace GeekApplication.Models.ContentWriterV3;

public sealed record ContentCampaignDto(
    Guid Id,
    Guid ClientId,
    string Name,
    string Keyword,
    string Status,
    Guid ProfileVersionId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    uint RowVersion);

public sealed record CreateContentCampaignCommand(
    Guid ClientId,
    string Name,
    string Keyword,
    Guid ProfileVersionId);

public sealed record UpdateContentCampaignStatusCommand(
    Guid Id,
    string Status);

public sealed record ContentAssetDto(
    Guid Id,
    Guid CampaignId,
    string Type,
    string Name,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateContentAssetCommand(
    Guid CampaignId,
    string Type,
    string Name);

public sealed record ContentAssetVersionDto(
    Guid Id,
    Guid AssetId,
    int VersionNumber,
    string BodyDocumentJson,
    uint RowVersion,
    DateTime CreatedAtUtc);

public sealed record CreateContentAssetVersionCommand(
    Guid AssetId,
    string BodyDocumentJson);

public sealed record UpdateContentAssetVersionCommand(
    Guid Id,
    string BodyDocumentJson);

public sealed record StrategyBriefDto(
    Guid Id,
    Guid CampaignId,
    Guid PainPointId,
    string AudienceProfile,
    string BuyingStage,
    string Angle,
    string CallToAction,
    string Status,
    uint RowVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateStrategyBriefCommand(
    Guid CampaignId,
    Guid PainPointId,
    string AudienceProfile,
    string BuyingStage,
    string Angle,
    string CallToAction);

public sealed record UpdateStrategyBriefCommand(
    Guid Id,
    string AudienceProfile,
    string BuyingStage,
    string Angle,
    string CallToAction);

public sealed record ApproveStrategyBriefCommand(
    Guid Id);

public sealed record RejectStrategyBriefCommand(
    Guid Id);

public sealed record PublicationDto(
    Guid Id,
    Guid AssetVersionId,
    string Status,
    Dictionary<string, object>? ResponseSnapshot,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreatePublicationCommand(
    Guid AssetVersionId);

public sealed record UpdatePublicationStatusCommand(
    Guid Id,
    string Status,
    Dictionary<string, object>? ResponseSnapshot,
    string? Error);
