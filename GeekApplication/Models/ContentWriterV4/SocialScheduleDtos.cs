namespace GeekApplication.Models.ContentWriterV4;

public sealed record SocialScheduleEntryDto(
    Guid Id,
    Guid OwnerId,
    Guid CampaignId,
    Guid AssetId,
    Guid AssetVersionId,
    string Channel,
    DateTime ScheduledAtUtc,
    string Status,
    string Title,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateSocialScheduleEntryCommand(
    Guid OwnerId,
    Guid CampaignId,
    Guid AssetId,
    Guid AssetVersionId,
    string Channel,
    DateTime ScheduledAtUtc,
    string Title,
    string? Notes);

public sealed record UpdateSocialScheduleEntryCommand(
    Guid Id,
    string Channel,
    DateTime ScheduledAtUtc,
    string Status,
    string Title,
    string? Notes);
