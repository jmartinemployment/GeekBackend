namespace GeekAPI.HttpClients;

public sealed record GccV2CreateDto(
    Guid Id,
    string OwnerUserId,
    string Title,
    string ContentType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? SiteSectionJson = null,
    string? SiteUrl = null);

public sealed record CreateGccV2CreateCommand(
    string OwnerUserId,
    string Title,
    string? ContentType,
    string? SiteSectionJson = null,
    string? SiteUrl = null);

public sealed record GccV2BriefDto(
    Guid Id,
    Guid CreateId,
    int Version,
    string TargetKeyword,
    string ContentType,
    string RawBriefJson,
    DateTimeOffset? FrozenAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateGccV2BriefCommand(Guid CreateId, string? TargetKeyword, string? ContentType, string? RawBriefJson);

public sealed record GccV2JobDto(
    Guid Id,
    string ContentType,
    Guid BriefId,
    string OwnerUserId,
    Guid CreateId,
    string Stage,
    string Status,
    int AttemptCount,
    string? ResultJson,
    string? Error,
    string? ClaimedByInstanceId,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? LeaseUntilUtc,
    int? TokensUsed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? SiteAnalysisProfileId = null);

public sealed record CreateGccV2JobCommand(
    Guid CreateId,
    string OwnerUserId,
    string? ContentType,
    Guid? BriefId,
    Guid? SiteAnalysisProfileId = null);

public sealed record PatchGccV2JobCommand(
    string? Stage = null,
    string? Status = null,
    string? ResultJson = null,
    string? Error = null,
    int? TokensUsed = null,
    bool? AttemptCountIncrement = null,
    bool? ReleaseClaim = null,
    DateTimeOffset? LeaseUntilUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    bool? Wake = null);

public sealed record GccV2JobEventDto(Guid Id, Guid JobId, int Seq, string Type, string PayloadJson, DateTimeOffset CreatedAtUtc);

public sealed record AppendGccV2JobEventCommand(string Type, string? PayloadJson, bool? Wake = null);

public sealed record GccV2StageResultDto(
    Guid Id,
    Guid JobId,
    string Stage,
    string? SectionKey,
    string OutputJson,
    int TokensUsed,
    DateTimeOffset CompletedAtUtc);

public sealed record CreateGccV2StageResultCommand(string Stage, string? SectionKey, string? OutputJson, int? TokensUsed);

public sealed record GccV2BrandKitDto(
    Guid Id,
    Guid? ClientId,
    Guid DerivedFromProfileId,
    int Version,
    string KitJson,
    string VoiceStatus,
    DateTimeOffset DerivedAtUtc,
    DateTimeOffset? AcceptedAtUtc);

public sealed record CreateGccV2BrandKitCommand(Guid DerivedFromProfileId, Guid? ClientId, string? KitJson, string? VoiceStatus);

public sealed record PatchGccV2BrandKitCommand(string? KitJson = null, string? VoiceStatus = null, DateTimeOffset? AcceptedAtUtc = null);

public sealed record GccV2OutlineDto(
    Guid Id,
    Guid BriefId,
    int Version,
    string OutlineJson,
    string HierarchyChildHeadingsJson,
    DateTimeOffset? FrozenAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateGccV2OutlineCommand(Guid BriefId, string? OutlineJson, string? HierarchyChildHeadingsJson);

public sealed record PatchGccV2OutlineCommand(
    string? OutlineJson = null,
    string? HierarchyChildHeadingsJson = null,
    DateTimeOffset? FrozenAtUtc = null);

public sealed record GccV2GuardrailRuleDto(
    Guid Id,
    string Pattern,
    string Action,
    string? ReplaceWith,
    bool Enabled,
    string? Scope,
    string? ReasonCode,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateGccV2GuardrailRuleCommand(
    string Pattern,
    string? Action,
    string? ReplaceWith,
    bool? Enabled,
    string? Scope,
    string? ReasonCode);

public sealed record PatchGccV2GuardrailRuleCommand(
    string? Pattern = null,
    string? Action = null,
    string? ReplaceWith = null,
    bool? Enabled = null,
    string? Scope = null,
    string? ReasonCode = null);

public sealed record GccV2PublishRecordDto(
    Guid Id,
    Guid CreateId,
    Guid JobId,
    string OwnerUserId,
    string Channel,
    string Status,
    int? ExternalPostId,
    string Slug,
    string? PublicUrl,
    string Title,
    string? MetaDescription,
    string? Error,
    string? BodyDocumentJson,
    bool IsPublished,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateGccV2PublishRecordCommand(
    Guid CreateId,
    Guid JobId,
    string OwnerUserId,
    string? Channel,
    string? Status,
    int? ExternalPostId,
    string? Slug,
    string? PublicUrl,
    string? Title,
    string? MetaDescription,
    string? Error,
    string? BodyDocumentJson,
    bool? IsPublished,
    DateTimeOffset? PublishedAtUtc);

public sealed record PatchGccV2PublishRecordCommand(
    string? Status = null,
    int? ExternalPostId = null,
    string? Slug = null,
    string? PublicUrl = null,
    string? Error = null,
    bool? IsPublished = null,
    DateTimeOffset? PublishedAtUtc = null);

public sealed record GccV2AiVisibilitySnapshotDto(
    Guid Id,
    Guid CreateId,
    Guid? JobId,
    string OwnerUserId,
    int Score,
    string ReportJson,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateGccV2AiVisibilitySnapshotCommand(
    Guid CreateId,
    Guid? JobId,
    string OwnerUserId,
    int Score,
    string? ReportJson);
