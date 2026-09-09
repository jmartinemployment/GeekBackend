using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>Stable, user-invokable capability. Separate from internal specialist team agents.</summary>
public sealed class GccV2TaskAgentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CapabilityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = "draft";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public List<GccV2TaskAgentVersion> Versions { get; set; } = [];
}

/// <summary>Immutable executable contract once published. Contract components are independently digest-pinned.</summary>
public sealed class GccV2TaskAgentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionId { get; set; }
    public string SemanticVersion { get; set; } = string.Empty;
    public string WorkflowGroup { get; set; } = string.Empty;
    public string FacetsJson { get; set; } = "{}";
    public string InputSchemaJson { get; set; } = "{}";
    public string InputSchemaDigest { get; set; } = string.Empty;
    public string OutputSchemaJson { get; set; } = "{}";
    public string OutputSchemaDigest { get; set; } = string.Empty;
    public string WorkflowJson { get; set; } = "{}";
    public string WorkflowDigest { get; set; } = string.Empty;
    public string ContextPolicyJson { get; set; } = "{}";
    public string ContextPolicyDigest { get; set; } = string.Empty;
    public string ResultRendererJson { get; set; } = "{}";
    public string ResultRendererDigest { get; set; } = string.Empty;
    public string CompatibleArtifactTypesJson { get; set; } = "{}";
    public string AllowedToolsJson { get; set; } = "[]";
    public string AllowedModelsJson { get; set; } = "[]";
    public string SkillVersionIdsJson { get; set; } = "[]";
    public string EvaluationThresholdsJson { get; set; } = "{}";
    public string EvaluationThresholdsDigest { get; set; } = string.Empty;
    public string VersionDigest { get; set; } = string.Empty;
    public string State { get; set; } = "draft";
    public string CreatedBy { get; set; } = string.Empty;
    public string? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? DeprecatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    [JsonIgnore] public GccV2TaskAgentDefinition Definition { get; set; } = null!;
}

public sealed class GccV2TaskRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid TaskAgentDefinitionId { get; set; }
    public Guid TaskAgentVersionId { get; set; }
    public string TaskAgentVersionDigest { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string InputDigest { get; set; } = string.Empty;
    public Guid? ContextManifestId { get; set; }
    public string? ContextManifestDigest { get; set; }
    public string ModelSnapshotJson { get; set; } = "{}";
    public string ModelSnapshotDigest { get; set; } = string.Empty;
    public string BudgetSnapshotJson { get; set; } = "{}";
    public string BudgetSnapshotDigest { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = "{}";
    public string SourceSnapshotDigest { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public string Phase { get; set; } = "queued";
    public int ProgressPercent { get; set; }
    public int AttemptCount { get; set; }
    public int RecoveryCount { get; set; }
    public Guid RootRunId { get; set; }
    public Guid? RetryOfRunId { get; set; }
    public string CreatedByActor { get; set; } = string.Empty;
    public string? LastActor { get; set; }
    public string? ClaimedByInstanceId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? TerminalError { get; set; }
    [JsonIgnore] public GccV2TaskAgentDefinition Definition { get; set; } = null!;
    [JsonIgnore] public GccV2TaskAgentVersion Version { get; set; } = null!;
    public List<GccV2TaskRunEvent> Events { get; set; } = [];
    public List<GccV2TaskArtifact> Artifacts { get; set; } = [];
}

public sealed class GccV2TaskRunEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int Seq { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2TaskRun Run { get; set; } = null!;
}

public sealed class GccV2TaskArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid RunId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2TaskRun Run { get; set; } = null!;
    public List<GccV2TaskArtifactVersion> Versions { get; set; } = [];
}

/// <summary>Immutable typed output snapshot. Corrections append versions; stored versions are never patched.</summary>
public sealed class GccV2TaskArtifactVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtifactId { get; set; }
    public int VersionNumber { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string EvidenceJson { get; set; } = "[]";
    public string CitationsJson { get; set; } = "[]";
    public string ValidationState { get; set; } = "pending";
    public string ValidationJson { get; set; } = "{}";
    public string Digest { get; set; } = string.Empty;
    public string CreatedByActor { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2TaskArtifact Artifact { get; set; } = null!;
    public List<GccV2TaskArtifactLineage> Parents { get; set; } = [];
    public List<GccV2TaskArtifactLineage> Children { get; set; } = [];
}

public sealed class GccV2TaskArtifactLineage
{
    public Guid ParentArtifactVersionId { get; set; }
    public Guid ChildArtifactVersionId { get; set; }
    public string Relationship { get; set; } = "derived-from";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2TaskArtifactVersion Parent { get; set; } = null!;
    [JsonIgnore] public GccV2TaskArtifactVersion Child { get; set; } = null!;
}
