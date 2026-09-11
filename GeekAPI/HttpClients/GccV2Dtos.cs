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
    string? AgentTeamSnapshotKeyId = null,
    Guid? Id = null,
    CreateGccV2RunContextManifestCommand? ContextManifest = null);

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
    DateTimeOffset? AcceptedAtUtc,
    string? OwnerUserId = null,
    string? CanonicalSha256 = null,
    string? AcceptedByUserId = null);

public sealed record CreateGccV2BrandKitCommand(
    Guid DerivedFromProfileId, Guid? ClientId, string? KitJson, string? VoiceStatus, string? OwnerUserId = null);

public sealed record PatchGccV2BrandKitCommand(
    string? KitJson = null, string? VoiceStatus = null, DateTimeOffset? AcceptedAtUtc = null,
    string? ActorUserId = null);

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

public sealed record GccV2TaskAgentDefinitionDto(
    Guid Id, string CapabilityId, string DisplayName, string Description, string LifecycleState,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<GccV2TaskAgentVersionDto> Versions);
public sealed record GccV2TaskAgentVersionDto(
    Guid Id, Guid DefinitionId, string SemanticVersion, string WorkflowGroup,
    string FacetsJson, string InputSchemaJson, string InputSchemaDigest,
    string OutputSchemaJson, string OutputSchemaDigest, string WorkflowJson, string WorkflowDigest,
    string ContextPolicyJson, string ContextPolicyDigest, string ResultRendererJson,
    string ResultRendererDigest, string CompatibleArtifactTypesJson, string AllowedToolsJson,
    string AllowedModelsJson, string SkillVersionIdsJson, string EvaluationThresholdsJson,
    string EvaluationThresholdsDigest, string VersionDigest, string State, string CreatedBy,
    string? ReviewedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? DeprecatedAtUtc, DateTimeOffset? RevokedAtUtc);
public sealed record GccV2TaskAgentLibraryPreferenceDto(
    Guid Id, string OwnerUserId, string FavoritesJson, string SavedConfigsJson, DateTimeOffset UpdatedAtUtc);
public sealed record PutGccV2TaskAgentLibraryPreferencesCommand(
    string OwnerUserId, string FavoritesJson, string SavedConfigsJson);
public sealed record GccV2GscConnectionDto(
    Guid Id,
    string OwnerUserId,
    string SiteUrl,
    string Status,
    byte[] EncryptedRefreshToken,
    byte[] EncryptionIv,
    byte[] EncryptionTag,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset UpdatedAtUtc);
public sealed record UpsertGccV2GscConnectionCommand(
    string OwnerUserId,
    string SiteUrl,
    string? Status,
    byte[]? EncryptedRefreshToken,
    byte[]? EncryptionIv,
    byte[]? EncryptionTag);
public sealed record GccV2CustomerOutcomeDto(
    Guid Id,
    string OwnerUserId,
    string Title,
    string MetricDefinition,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Baseline,
    string? Denominator,
    string? ObservedValue,
    string Source,
    string EvidenceStatus,
    string? AttributionMethod,
    double? AttributionConfidence,
    string WorkflowVersionsJson,
    int? GeneratedCount,
    int? AcceptedCount,
    int? PublishedCount,
    int? RejectedCount,
    int? ReviewMinutes,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
public sealed record CreateGccV2CustomerOutcomeCommand(
    string OwnerUserId,
    string Title,
    string MetricDefinition,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Baseline,
    string? Denominator,
    string? ObservedValue,
    string Source,
    string? EvidenceStatus,
    string? AttributionMethod,
    double? AttributionConfidence,
    string? WorkflowVersionsJson,
    int? GeneratedCount,
    int? AcceptedCount,
    int? PublishedCount,
    int? RejectedCount,
    int? ReviewMinutes,
    string? Notes);
public sealed record GccV2TaskRunDto(
    Guid Id, string OwnerUserId, Guid TaskAgentDefinitionId, Guid TaskAgentVersionId,
    string TaskAgentVersionDigest, string InputJson, string InputDigest, Guid? ContextManifestId,
    string? ContextManifestDigest, string ModelSnapshotJson, string ModelSnapshotDigest,
    string BudgetSnapshotJson, string BudgetSnapshotDigest, string SourceSnapshotJson,
    string SourceSnapshotDigest, string Status, string Phase, int ProgressPercent, int AttemptCount,
    int RecoveryCount, Guid RootRunId, Guid? RetryOfRunId, string CreatedByActor, string? LastActor,
    string? ClaimedByInstanceId, DateTimeOffset? ClaimedAtUtc, DateTimeOffset? LeaseUntilUtc,
    DateTimeOffset? CancellationRequestedAtUtc, DateTimeOffset? CancelledAtUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? CompletedAtUtc,
    string? TerminalError, IReadOnlyList<GccV2TaskRunEventDto>? Events = null,
    IReadOnlyList<GccV2TaskArtifactDto>? Artifacts = null);
public sealed record GccV2TaskRunEventDto(
    Guid Id, Guid RunId, int Seq, string Type, string PayloadJson, string Actor,
    DateTimeOffset CreatedAtUtc);
public sealed record GccV2TaskArtifactDto(
    Guid Id, string OwnerUserId, Guid RunId, string ArtifactType, Guid? CurrentVersionId,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<GccV2TaskArtifactVersionDto> Versions);
public sealed record GccV2TaskArtifactVersionDto(
    Guid Id, Guid ArtifactId, int VersionNumber, string PayloadJson, string EvidenceJson,
    string CitationsJson, string ValidationState, string ValidationJson, string Digest,
    string CreatedByActor, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<GccV2TaskArtifactLineageDto>? Parents = null,
    IReadOnlyList<GccV2TaskArtifactLineageDto>? Children = null);
public sealed record GccV2TaskArtifactLineageDto(
    Guid ParentArtifactVersionId, Guid ChildArtifactVersionId, string Relationship,
    DateTimeOffset CreatedAtUtc);
public sealed record CreateGccV2TaskAgentDefinitionCommand(
    string CapabilityId, string DisplayName, string Description, string Actor);
public sealed record PatchGccV2TaskAgentDefinitionCommand(
    string? DisplayName, string? Description, string Actor);
public sealed record CreateGccV2TaskAgentVersionCommand(
    string SemanticVersion, string WorkflowGroup, string FacetsJson, string InputSchemaJson,
    string OutputSchemaJson, string WorkflowJson, string ContextPolicyJson,
    string ResultRendererJson, string CompatibleArtifactTypesJson, string AllowedToolsJson,
    string AllowedModelsJson, string SkillVersionIdsJson, string EvaluationThresholdsJson,
    string Actor);
public sealed record TransitionGccV2TaskAgentVersionCommand(string Actor, string? Reason);
public sealed record CreateGccV2TaskRunCommand(
    string OwnerUserId, Guid TaskAgentDefinitionId, Guid TaskAgentVersionId,
    string TaskAgentVersionDigest, string InputJson, string InputDigest,
    Guid? ContextManifestId, string? ContextManifestDigest,
    string ModelSnapshotJson, string ModelSnapshotDigest,
    string BudgetSnapshotJson, string BudgetSnapshotDigest,
    string SourceSnapshotJson, string SourceSnapshotDigest, Guid? RetryOfRunId, string Actor);
public sealed record TransitionGccV2TaskRunCommand(
    string Status, string Phase, int ProgressPercent, string EventType,
    string? EventPayloadJson, string Actor, string? ExpectedClaimedBy,
    DateTimeOffset? LeaseUntilUtc, string? TerminalError, bool CancellationRequested = false);
public sealed record AddGccV2TaskRunEventCommand(string Type, string? PayloadJson, string Actor);
public sealed record CreateGccV2TaskArtifactCommand(
    string OwnerUserId, string ArtifactType, string Actor, string? ExpectedClaimedBy = null);
public sealed record CreateGccV2TaskArtifactVersionCommand(
    string OwnerUserId, string PayloadJson, string EvidenceJson, string CitationsJson,
    string ValidationState, string ValidationJson, IReadOnlyList<Guid> ParentArtifactVersionIds,
    string Actor, string? Relationship = null, Guid? Id = null, string? ExpectedClaimedBy = null);

public sealed record GccV2KnowledgeAssetDto(
    Guid Id, string OwnerUserId, string Name, string? Description, Guid? CurrentVersionId,
    bool IsRetired, uint Revision, string TagsJson, string Kind, string Visibility,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<GccV2KnowledgeAssetVersionDto> Versions);
public sealed record GccV2KnowledgeAssetVersionDto(
    Guid Id, Guid AssetId, int VersionNumber, int SchemaVersion, string CanonicalSha256,
    string LifecycleState, string CreatedBy, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc, string? ReviewedBy, DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveUntilUtc, string SourceDescriptorJson, string ContentSha256,
    string MediaType, string? Language, DateTimeOffset? SourceModifiedAtUtc,
    string ExtractionState, string IndexState, string ProvenanceJson,
    IReadOnlyList<GccV2KnowledgeResourceDto>? Resources = null);
public sealed record GccV2KnowledgeResourceDto(
    Guid Id, Guid KnowledgeAssetVersionId, string ResourceKind, string ObjectKey,
    long ByteSize, string Sha256, string MediaType, string SafeFileName,
    string? ParserName, string? ParserVersion, string? ExtractionSha256,
    string CoordinatesJson, string ScanState, DateTimeOffset CreatedAtUtc);
public sealed record CreateGccV2KnowledgeCommand(
    string OwnerUserId, string Name, string? Description, string? TagsJson, string? Kind, string ActorUserId);
public sealed record PatchGccV2ContextCatalogCommand(
    string OwnerUserId, string ActorUserId, string? Name = null, string? Description = null, bool? IsRetired = null);
public sealed record CreateGccV2KnowledgeVersionCommand(
    string OwnerUserId, int SchemaVersion, string CanonicalSha256, string ContentSha256,
    string MediaType, string? Language, string? SourceDescriptorJson, string? ProvenanceJson,
    DateTimeOffset? SourceModifiedAtUtc, string ActorUserId);
public sealed record AddGccV2KnowledgeResourceCommand(
    string OwnerUserId, string ResourceKind, string ObjectKey, long ByteSize, string Sha256,
    string MediaType, string SafeFileName, string ScanState, string? ParserName = null,
    string? ParserVersion = null, string? ExtractionSha256 = null, string? CoordinatesJson = null);
public sealed record TransitionGccV2ContextCommand(string OwnerUserId, string ActorUserId, string? Reason = null);
public sealed record CreateGccV2ContextCatalogCommand(
    string Kind, string OwnerUserId, string Name, string? Description, string ActorUserId);
public sealed record CreateGccV2ContextCatalogVersionCommand(
    string OwnerUserId, int SchemaVersion, string CanonicalSha256, string PayloadJson,
    string? Locale, string? ProvenanceJson, Guid? ProductSchemaVersionId,
    string? ApprovedClaimsJson, string? ProhibitedClaimsJson, string? MandatoryDisclaimersJson,
    DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveUntilUtc, string ActorUserId);
public sealed record GccV2GovernedVersionLookupDto(
    string Kind, Guid StableId, Guid VersionId, int VersionNumber, int SchemaVersion,
    string CanonicalSha256, string LifecycleState, DateTimeOffset? SourceModifiedAtUtc,
    DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveUntilUtc,
    string ExtractionState = "ready", string IndexState = "ready", string? PayloadJson = null,
    string? ApprovedClaimsJson = null, string? ProhibitedClaimsJson = null,
    string? MandatoryDisclaimersJson = null, Guid? ProductSchemaVersionId = null);

public sealed record GccV2RunAttachmentDto(
    Guid Id, string OwnerUserId, Guid CreateId, string ObjectKey, string SafeFileName,
    string MediaType, long ByteSize, string Sha256, string IngestionState,
    DateTimeOffset UploadExpiresAtUtc, DateTimeOffset RetainUntilUtc,
    DateTimeOffset? FinalizedAtUtc, DateTimeOffset? DeletedAtUtc, DateTimeOffset CreatedAtUtc);
public sealed record CreateGccV2RunAttachmentCommand(
    string OwnerUserId, Guid CreateId, string ObjectKey, string SafeFileName, string MediaType,
    long ByteSize, string Sha256, DateTimeOffset UploadExpiresAtUtc, DateTimeOffset RetainUntilUtc);
public sealed record FinalizeGccV2RunAttachmentCommand(string OwnerUserId, long ByteSize, string Sha256);
public sealed record GccV2ContextSelectionDto(
    Guid Id, string OwnerUserId, Guid? CreateId, string SelectionJson,
    string CreatedBy, DateTimeOffset CreatedAtUtc);
public sealed record CreateGccV2ContextSelectionCommand(
    string OwnerUserId, Guid? CreateId, string SelectionJson, string ActorUserId);

public sealed record GccV2RunContextManifestDto(
    Guid Id, string OwnerUserId, Guid JobId, int Attempt, int SchemaVersion,
    string CanonicalJson, string Sha256, string Signature, string SigningKeyId,
    string ResolverIdentity, DateTimeOffset ResolvedAtUtc, Guid? ReplacesManifestId,
    IReadOnlyList<GccV2RunContextManifestEntryDto> Entries);
public sealed record GccV2RunContextManifestEntryDto(
    Guid Id, Guid ManifestId, string ContextKind, Guid StableId, Guid? VersionId,
    int? VersionNumber, string ContentSha256, string LifecycleDecision,
    string PermissionDecision, string FreshnessDecision, string SelectionSource,
    string? SelectedFieldIdsJson, DateTimeOffset? SourceModifiedAtUtc);
public sealed record CreateGccV2RunContextManifestCommand(
    Guid Id, string OwnerUserId, Guid JobId, int Attempt, int SchemaVersion, string CanonicalJson,
    string Sha256, string Signature, string SigningKeyId, string ResolverIdentity,
    DateTimeOffset ResolvedAtUtc, Guid? ReplacesManifestId,
    IReadOnlyList<CreateGccV2RunContextManifestEntryCommand> Entries);
public sealed record CreateGccV2RunContextManifestEntryCommand(
    string ContextKind, Guid StableId, Guid? VersionId, int? VersionNumber, string ContentSha256,
    string LifecycleDecision, string PermissionDecision, string FreshnessDecision,
    string SelectionSource, string? SelectedFieldIdsJson, DateTimeOffset? SourceModifiedAtUtc);
public sealed record GccV2ContextIngestionJobDto(
    Guid Id, string OwnerUserId, string TargetKind, Guid TargetId, string Status,
    int ProgressPercent, int AttemptCount, string? ClaimedByInstanceId,
    DateTimeOffset? ClaimedAtUtc, DateTimeOffset? LeaseUntilUtc, DateTimeOffset? HeartbeatAtUtc,
    string? TerminalError, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc, IReadOnlyList<GccV2ContextIngestionEventDto>? Events = null);
public sealed record GccV2ContextIngestionEventDto(
    Guid Id, Guid JobId, int Seq, string Type, string PayloadJson, DateTimeOffset CreatedAtUtc);
public sealed record GccV2ContextQuotaUsageDto(
    long KnowledgeBytes, long OwnerAttachmentBytes, long RunAttachmentBytes);
public sealed record QueueGccV2KnowledgeIngestionCommand(
    string OwnerUserId, string ObjectKey, long ByteSize, string Sha256);
public sealed record TransitionGccV2ContextIngestionJobCommand(
    string Status, int ProgressPercent, string EventType, string? EventPayloadJson = null,
    string? TerminalError = null, string? IndexState = null);

// Durable canvas projects (multi-asset owner-scoped workspaces).

public sealed record GccV2CanvasProjectListItemDto(
    Guid Id, string OwnerUserId, string Name, string Description, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ActivityJson, int AssetCount);

public sealed record GccV2CanvasProjectDto(
    Guid Id, string OwnerUserId, string Name, string Description, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ActivityJson,
    IReadOnlyList<GccV2CanvasAssetDto> Assets);

public sealed record GccV2CanvasAssetDto(
    Guid Id, Guid ProjectId, string Title, string Kind, string ParentAssetIdsJson,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<GccV2CanvasAssetVersionDto> Versions);

public sealed record GccV2CanvasAssetVersionDto(
    Guid Id, Guid AssetId, int VersionNumber, string Status, string Summary,
    string EvidenceJson, string ProvenanceJson, string CreatedBy, DateTimeOffset CreatedAtUtc);

public sealed record CreateGccV2CanvasProjectCommand(
    string OwnerUserId, string Name, string? Description = null, string? Status = null,
    string? ActivityJson = null);

public sealed record PatchGccV2CanvasProjectCommand(
    string OwnerUserId, string? Name = null, string? Description = null, string? Status = null,
    string? ActivityJson = null);

public sealed record CreateGccV2CanvasAssetCommand(
    string OwnerUserId, string Title, string? Kind = null, IReadOnlyList<Guid>? ParentAssetIds = null);

public sealed record AppendGccV2CanvasAssetVersionCommand(
    string OwnerUserId, string CreatedBy, string? Status = null, string? Summary = null,
    string? EvidenceJson = null, string? ProvenanceJson = null);

// Durable batch grids (owner-scoped work-item rows + stub runs).

public sealed record GccV2GridListItemDto(
    Guid Id, string OwnerUserId, string Name, string Description, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ConfigJson,
    int RowCount, string? LastRunStatus);

public sealed record GccV2GridDto(
    Guid Id, string OwnerUserId, string Name, string Description, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ConfigJson,
    IReadOnlyList<GccV2GridRowDto> Rows, IReadOnlyList<GccV2GridRunDto> Runs);

public sealed record GccV2GridRowDto(
    Guid Id, Guid GridId, int RowIndex, string InputJson, string? OutputJson,
    string Status, string? Error, DateTimeOffset UpdatedAtUtc);

public sealed record GccV2GridRunDto(
    Guid Id, Guid GridId, string Mode, int? SampleSize, string Status, string ActorUserId,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    string BudgetPreviewJson, string HistoryJson, int OutputCount);

public sealed record CreateGccV2GridCommand(
    string OwnerUserId, string Name, string? Description = null, string? Status = null,
    string? ConfigJson = null, bool? SeedDemo = null, string? Capability = null);

public sealed record PatchGccV2GridCommand(
    string OwnerUserId, string? Name = null, string? Description = null, string? Status = null,
    string? ConfigJson = null);

public sealed record CreateGccV2GridRowCommand(
    string OwnerUserId, string? InputJson = null);

public sealed record CreateGccV2GridRowsBulkCommand(
    string OwnerUserId, IReadOnlyList<string> InputJsons);

public sealed record CreateGccV2GridRunCommand(
    string OwnerUserId, string? Mode = null, int? SampleSize = null, string? ActorUserId = null,
    IReadOnlyDictionary<string, string>? RowArtifactJsonByRowId = null);

// Geek Content Pipelines (Plan → Create → Adapt → Activate → Optimize).

public sealed record GccV2PipelineListItemDto(
    Guid Id, string Name, string Description, string Status, int VersionNumber,
    string Digest, DateTimeOffset UpdatedAtUtc, int RunCount);

public sealed record GccV2PipelineDto(
    Guid Id, string OwnerUserId, string Name, string Description, string Status,
    int VersionNumber, string Digest, string StagesJson, string PolicyJson,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<GccV2PipelineRunDto> Runs);

public sealed record GccV2PipelineRunDto(
    Guid Id, int DefinitionVersionNumber, string DefinitionDigest, string Status,
    string ActorUserId, string InputJson, string HistoryJson,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? PausedAtUtc,
    string? Error, IReadOnlyList<GccV2PipelineWorkItemDto> WorkItems);

public sealed record GccV2PipelineWorkItemDto(
    Guid Id, int WorkItemIndex, string InputJson, string Status, string? Error,
    DateTimeOffset UpdatedAtUtc, IReadOnlyList<GccV2PipelineStageAttemptDto> StageAttempts);

public sealed record GccV2PipelineStageAttemptDto(
    Guid Id, string StageKey, string LifecycleStage, string Kind, string DisplayName,
    string? CapabilityId, string? Handoff, int AttemptNumber, string Status,
    string? OutputJson, string? Error, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    Guid? TaskRunId = null, Guid? ArtifactVersionId = null);

public sealed record CreateGccV2PipelineCommand(
    string OwnerUserId, string Name, string? Description, string StagesJson, string Digest,
    string? PolicyJson = null, bool? Publish = true);

public sealed record StartGccV2PipelineRunCommand(
    string OwnerUserId, string? ActorUserId = null, string? InputJson = null,
    string? FailStageKey = null, IReadOnlyList<string>? WorkItemInputsJson = null);

public sealed record GccV2PipelineActorCommand(string OwnerUserId, string? ActorUserId = null);
