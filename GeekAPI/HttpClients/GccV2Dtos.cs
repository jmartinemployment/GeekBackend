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
    Guid? SiteAnalysisProfileId = null,
    string? InitialStage = null);

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

/// <summary>Atomically patch a job row and/or append one event — single DB transaction.</summary>
public sealed record ApplyGccV2JobTransitionCommand(
    string? Stage = null,
    string? Status = null,
    string? ResultJson = null,
    string? Error = null,
    int? TokensUsed = null,
    bool? AttemptCountIncrement = null,
    bool? ReleaseClaim = null,
    DateTimeOffset? LeaseUntilUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? EventType = null,
    string? EventPayloadJson = null,
    bool? Wake = null);

public sealed record GccV2JobTransitionResultDto(GccV2JobDto Job, GccV2JobEventDto? Event);

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

public sealed record GccV2PartnerResearchRecordDto(
    Guid Id,
    Guid CreateId,
    Guid? JobId,
    string TargetUrl,
    string HostDomain,
    DateTimeOffset CrawledAtUtc,
    bool IsSuccess,
    string CrawlStatusLog,
    string? ExtractedTitle,
    string? PageJson,
    string? FlattenedTextContent);

public sealed record CreateGccV2PartnerResearchRecordCommand(
    Guid CreateId,
    string TargetUrl,
    bool IsSuccess,
    string? CrawlStatusLog = null,
    string? HostDomain = null,
    Guid? JobId = null,
    string? ExtractedTitle = null,
    string? PageJson = null,
    string? FlattenedTextContent = null);

public sealed record GccV2ToolSourceCrawlRunDto(
    Guid Id,
    string OwnerUserId,
    string Status,
    string SeedUrlsJson,
    string? HostProgressJson,
    string? PartnerResearchJson,
    string? ErrorSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CreateGccV2ToolSourceCrawlRunCommand(string OwnerUserId, string? SeedUrlsJson);

public sealed record PatchGccV2ToolSourceCrawlRunCommand(
    string? Status = null,
    string? HostProgressJson = null,
    string? PartnerResearchJson = null,
    string? ErrorSummary = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

public sealed record GccV2ToolSourceCrawlPageDto(
    Guid Id,
    Guid RunId,
    string Origin,
    string Url,
    string FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html,
    DateTimeOffset CrawledAtUtc);

public sealed record CreateGccV2ToolSourceCrawlPageItemCommand(
    string Origin,
    string Url,
    string? FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html);

public sealed record CreateGccV2ToolSourceCrawlPageBatchCommand(
    Guid RunId,
    IReadOnlyList<CreateGccV2ToolSourceCrawlPageItemCommand> Pages);

public sealed record PatchGccV2BriefCommand(string? RawBriefJson);
