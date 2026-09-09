namespace GeekAPI.HttpClients;

public sealed record GccV2CreateDto(
    Guid Id,
    string OwnerUserId,
    string Title,
    string ContentType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? SiteSectionJson = null,
    string? SiteUrl = null,
    Guid? ProjectSiteCrawlRunId = null,
    string? SelectedAgentVersionIdsJson = null);

public sealed record CreateGccV2CreateCommand(
    string OwnerUserId,
    string Title,
    string? ContentType,
    string? SiteSectionJson = null,
    string? SiteUrl = null,
    Guid? ProjectSiteCrawlRunId = null,
    string? SelectedAgentVersionIdsJson = null);

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
    Guid? SiteAnalysisProfileId = null,
    Guid? ProjectSiteCrawlRunId = null,
    string? AgentTeamSnapshotJson = null,
    string? AgentTeamSnapshotDigest = null,
    string? AgentTeamSnapshotSignature = null,
    string? AgentTeamSnapshotKeyId = null);

public sealed record CreateGccV2JobCommand(
    Guid CreateId,
    string OwnerUserId,
    string? ContentType,
    Guid? BriefId,
    Guid? SiteAnalysisProfileId = null,
    Guid? ProjectSiteCrawlRunId = null,
    string? InitialStage = null,
    IReadOnlyList<Guid>? AgentVersionIds = null,
    string? AgentTeamSnapshotJson = null,
    string? AgentTeamSnapshotDigest = null,
    string? AgentTeamSnapshotSignature = null,
    string? AgentTeamSnapshotKeyId = null);

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

public sealed record PatchGccV2BriefCommand(string? RawBriefJson);

public sealed record GccV2ProjectSiteCrawlRunDto(
    Guid Id,
    string OwnerUserId,
    string SiteUrl,
    string Status,
    string SeedUrlsJson,
    string? HostProgressJson,
    string? ErrorSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CreateGccV2ProjectSiteCrawlRunCommand(
    string OwnerUserId,
    string SiteUrl,
    string? SeedUrlsJson);

public sealed record PatchGccV2ProjectSiteCrawlRunCommand(
    string? Status = null,
    string? HostProgressJson = null,
    string? ErrorSummary = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

public sealed record GccV2ProjectSiteCrawlPageDto(
    Guid Id,
    Guid RunId,
    string Origin,
    string Url,
    string FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html,
    DateTimeOffset CrawledAtUtc);

public sealed record CreateGccV2ProjectSiteCrawlPageBatchCommand(
    Guid RunId,
    IReadOnlyList<CreateGccV2ProjectSiteCrawlPageItemCommand> Pages);

public sealed record CreateGccV2ProjectSiteCrawlPageItemCommand(
    string Origin,
    string Url,
    string? FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html);

public sealed record GccV2ProjectSiteCrawlPageBatchResult(
    int Count,
    IReadOnlyList<GccV2ProjectSiteCrawlCreatedPageDto> Pages);

public sealed record GccV2ProjectSiteCrawlCreatedPageDto(string Url, Guid PageId);

public sealed record CreateGccV2ProjectSiteCrawlLinkBatchCommand(
    Guid RunId,
    IReadOnlyList<CreateGccV2ProjectSiteCrawlLinkItemCommand> Links);

public sealed record CreateGccV2ProjectSiteCrawlLinkItemCommand(
    Guid PageId,
    string FromUrl,
    string LinkUrl,
    bool IsSameOrigin);

public sealed record GccV2ProjectSiteCrawlPageActivityDto(
    int PageCount,
    DateTimeOffset? LastCrawledAtUtc);

public sealed record GccV2SkillPackageDto(
    Guid Id, string Slug, string DisplayName, string Description, string SourceRepository,
    string SourcePath, string Publisher, string LifecycleState, bool IsFirstParty,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? DeprecatedAtUtc,
    IReadOnlyList<GccV2SkillVersionDto> Versions);

public sealed record GccV2SkillVersionDto(
    Guid Id, Guid PackageId, string SemanticVersion, string ImmutableGitRef,
    string PackageSha256, string ManifestDigest, string License, string Compatibility,
    string State, DateTimeOffset ImportedAtUtc, DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? PublishedAtUtc, string? Reviewer, string? ReviewNotes,
    Guid? SupersedesVersionId, IReadOnlyList<GccV2SkillFileDto> Files,
    IReadOnlyList<GccV2SkillApplicabilityDto> Applicability,
    IReadOnlyList<GccV2SkillReviewFindingDto> Findings);

public sealed record GccV2SkillFileDto(
    Guid Id, Guid VersionId, string RelativePath, string MediaType,
    long ByteCount, string Sha256, string Content);
public sealed record GccV2SkillApplicabilityDto(
    Guid Id, Guid VersionId, string Stage, string ContentType, int Order,
    string ConflictsJson, string RequiredToolsJson, string ActivationMode);
public sealed record GccV2SkillReviewFindingDto(
    Guid Id, Guid VersionId, string Severity, string Scanner, string Rule,
    string? FilePath, int? Line, string Message, string Disposition,
    string? ReviewerRationale, bool PermanentRejection);
public sealed record GccV2SkillAuditEventDto(
    Guid Id, Guid PackageId, Guid? VersionId, string Actor, string Action,
    string? SourceIp, string? RequestId, string? BeforeState, string? AfterState,
    DateTimeOffset CreatedAtUtc);

public sealed record ImportGccV2SkillCommand(
    string Slug, string DisplayName, string Description, string SourceRepository, string SourcePath,
    string Publisher, string SemanticVersion, string ImmutableGitRef, string PackageSha256,
    string ManifestDigest, string License, string Compatibility, bool IsFirstParty,
    bool PermanentRejection, IReadOnlyList<ImportGccV2SkillFile> Files,
    IReadOnlyList<ImportGccV2SkillApplicability> Applicability,
    IReadOnlyList<ImportGccV2SkillFinding> Findings,
    string Actor, string? SourceIp, string? RequestId);
public sealed record ImportGccV2SkillFile(
    string RelativePath, string MediaType, long ByteCount, string Sha256, string Content);
public sealed record ImportGccV2SkillApplicability(
    string Stage, string ContentType, int Order, string ConflictsJson,
    string RequiredToolsJson, string ActivationMode);
public sealed record ImportGccV2SkillFinding(
    string Severity, string Scanner, string Rule, string? FilePath, int? Line,
    string Message, bool PermanentRejection);
public sealed record GccV2SkillFindingDisposition(Guid FindingId, string Disposition, string Rationale);
public sealed record PatchGccV2SkillFindingCommand(
    string Disposition, string ReviewerRationale, string Actor, string? SourceIp, string? RequestId);
public sealed record ReviewGccV2SkillCommand(
    bool Approve, string? Notes, IReadOnlyList<GccV2SkillFindingDisposition> Findings,
    string Actor, string? SourceIp, string? RequestId);
public sealed record TransitionGccV2SkillCommand(string Actor, string? SourceIp, string? RequestId);

public sealed record GccV2AgentDto(
    Guid Id, string Slug, string DisplayName, string Description, string LifecycleState,
    bool IsFirstParty, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<GccV2AgentVersionDto> Versions);
public sealed record GccV2AgentVersionDto(
    Guid Id, Guid AgentId, string SemanticVersion, string Instructions,
    string ContentTypesJson, string AllowedToolsJson, string AllowedModelsJson,
    string VersionDigest, string State, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc, DateTimeOffset? TestedAtUtc,
    DateTimeOffset? PublishedAtUtc, DateTimeOffset? DeprecatedAtUtc,
    DateTimeOffset? RevokedAtUtc, string? Reviewer, string? ReviewNotes,
    string? TestResultJson, IReadOnlyList<GccV2AgentSkillVersionDto> Skills,
    IReadOnlyList<GccV2AgentStageParticipationDto> StageParticipation,
    string Objective = "", string ModelPolicyVersion = "content-model-policy.v1",
    IReadOnlyList<GccV2AgentReviewFindingDto>? Findings = null,
    string ModelPolicyProfile = "default");
public sealed record GccV2AgentReviewFindingDto(
    Guid Id, Guid AgentVersionId, string Severity, string Rule, string Message,
    string Disposition, string? ReviewerRationale, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DisposedAtUtc);
public sealed record GccV2AgentSkillVersionDto(
    Guid AgentVersionId, Guid SkillVersionId, int Order, GccV2AgentAssignedSkillVersionDto SkillVersion);
public sealed record GccV2AgentAssignedSkillVersionDto(
    Guid Id, string SemanticVersion, string PackageSha256, string State,
    GccV2AgentAssignedSkillPackageDto Package,
    IReadOnlyList<GccV2SkillApplicabilityDto> Applicability);
public sealed record GccV2AgentAssignedSkillPackageDto(Guid Id, string Slug, string DisplayName);
public sealed record GccV2AgentStageParticipationDto(
    Guid Id, Guid AgentVersionId, string Stage, string Role, int Order);
public sealed record GccV2AgentAuditEventDto(
    Guid Id, Guid AgentId, Guid? AgentVersionId, string Actor, string Action,
    string? BeforeState, string? AfterState, string? DetailJson,
    string? SourceIp, string? RequestId, DateTimeOffset CreatedAtUtc);
public sealed record GccV2AgentTestRunDto(
    Guid Id, Guid AgentVersionId, string VersionDigest, string Scenario, string InputJson,
    string Status, int ProgressPercent, string Phase, string? ResultJson, string? Error,
    string RequestedBy, DateTimeOffset QueuedAtUtc, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, DateTimeOffset UpdatedAtUtc,
    string? ClaimedByInstanceId, DateTimeOffset? ClaimedAtUtc, DateTimeOffset? LeaseUntilUtc,
    int AttemptCount, int RecoveryCount, DateTimeOffset? CancellationRequestedAtUtc,
    DateTimeOffset? CancelledAtUtc);
public sealed record CreateGccV2AgentCommand(
    string Slug, string DisplayName, string Description, bool IsFirstParty,
    string Actor, string? SourceIp, string? RequestId);
public sealed record PatchGccV2AgentCommand(
    string? DisplayName, string? Description, string Actor, string? SourceIp, string? RequestId);
public sealed record CreateGccV2AgentVersionCommand(
    string SemanticVersion, string Instructions, IReadOnlyList<string> ContentTypes,
    IReadOnlyList<string> AllowedTools, IReadOnlyList<string> AllowedModels,
    IReadOnlyList<Guid> SkillVersionIds,
    IReadOnlyList<CreateGccV2AgentStageParticipation> StageParticipation,
    string Actor, string? SourceIp, string? RequestId,
    string? Objective = null, string? ModelPolicyVersion = null,
    string? ModelPolicyProfile = null);
public sealed record PatchGccV2AgentFindingCommand(
    string Disposition, string ReviewerRationale, string Actor, string? SourceIp, string? RequestId);
public sealed record CreateGccV2AgentSuccessorCommand(
    string SemanticVersion, string? Objective, string? Instructions,
    IReadOnlyList<string>? ContentTypes, IReadOnlyList<string>? AllowedTools,
    IReadOnlyList<string>? AllowedModels, IReadOnlyList<Guid>? SkillVersionIds,
    IReadOnlyList<CreateGccV2AgentStageParticipation>? StageParticipation,
    string? ModelPolicyVersion, string Actor, string? SourceIp, string? RequestId,
    string? ModelPolicyProfile = null);
public sealed record CreateGccV2AgentStageParticipation(string Stage, string Role, int Order);
public sealed record ReviewGccV2AgentVersionCommand(
    bool Approve, string? Notes, string Actor, string? SourceIp, string? RequestId);
public sealed record QueueGccV2AgentTestRunCommand(
    string Scenario, string InputJson, string Actor, string? SourceIp, string? RequestId);
public sealed record PatchGccV2AgentTestRunCommand(
    string? Status, int? ProgressPercent, string? Phase, string? ResultJson, string? Error,
    DateTimeOffset? LeaseUntilUtc, bool? CancellationRequested,
    string Actor, string? SourceIp, string? RequestId);
public sealed record TransitionGccV2AgentVersionCommand(
    string Actor, string? Reason, string? SourceIp, string? RequestId);
