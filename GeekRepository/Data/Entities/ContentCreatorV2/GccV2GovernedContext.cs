using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

public static class GccV2ContextLifecycle
{
    public const string Draft = "draft";
    public const string InReview = "in_review";
    public const string Approved = "approved";
    public const string Deprecated = "deprecated";
    public const string Revoked = "revoked";
}

[NotMapped]
public abstract class GccV2OwnedCatalog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public bool IsRetired { get; set; }
    public uint Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

[NotMapped]
public abstract class GccV2GovernedVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int VersionNumber { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CanonicalSha256 { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = GccV2ContextLifecycle.Draft;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveUntilUtc { get; set; }
}

public sealed class GccV2KnowledgeAsset : GccV2OwnedCatalog
{
    public string TagsJson { get; set; } = "[]";
    public string Kind { get; set; } = "document";
    public string Visibility { get; set; } = "private";
    public List<GccV2KnowledgeAssetVersion> Versions { get; set; } = [];
}

public sealed class GccV2KnowledgeAssetVersion : GccV2GovernedVersion
{
    public Guid AssetId { get; set; }
    public string SourceDescriptorJson { get; set; } = "{}";
    public string ContentSha256 { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public string? Language { get; set; }
    public DateTimeOffset? SourceModifiedAtUtc { get; set; }
    public string ExtractionState { get; set; } = "pending";
    public string IndexState { get; set; } = "pending";
    public string ProvenanceJson { get; set; } = "{}";
    [JsonIgnore] public GccV2KnowledgeAsset Asset { get; set; } = null!;
    public List<GccV2KnowledgeResource> Resources { get; set; } = [];
}

public sealed class GccV2KnowledgeResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KnowledgeAssetVersionId { get; set; }
    public string ResourceKind { get; set; } = "original";
    public string ObjectKey { get; set; } = string.Empty;
    public long ByteSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public string SafeFileName { get; set; } = string.Empty;
    public string? ParserName { get; set; }
    public string? ParserVersion { get; set; }
    public string? ExtractionSha256 { get; set; }
    public string CoordinatesJson { get; set; } = "{}";
    public string ScanState { get; set; } = "quarantined";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2KnowledgeAssetVersion Version { get; set; } = null!;
}

public sealed class GccV2Audience : GccV2OwnedCatalog
{
    public List<GccV2AudienceVersion> Versions { get; set; } = [];
}

public sealed class GccV2AudienceVersion : GccV2GovernedVersion
{
    public Guid AudienceId { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public string Locale { get; set; } = "en";
    public string ProvenanceJson { get; set; } = "{}";
    [JsonIgnore] public GccV2Audience Audience { get; set; } = null!;
}

public sealed class GccV2StyleGuide : GccV2OwnedCatalog
{
    public List<GccV2StyleGuideVersion> Versions { get; set; } = [];
}

public sealed class GccV2StyleGuideVersion : GccV2GovernedVersion
{
    public Guid StyleGuideId { get; set; }
    public string PolicyJson { get; set; } = "{}";
    public string Locale { get; set; } = "en";
    [JsonIgnore] public GccV2StyleGuide StyleGuide { get; set; } = null!;
}

public sealed class GccV2VisualGuideline : GccV2OwnedCatalog
{
    public List<GccV2VisualGuidelineVersion> Versions { get; set; } = [];
}

public sealed class GccV2VisualGuidelineVersion : GccV2GovernedVersion
{
    public Guid VisualGuidelineId { get; set; }
    public string PolicyJson { get; set; } = "{}";
    public string Locale { get; set; } = "en";
    [JsonIgnore] public GccV2VisualGuideline VisualGuideline { get; set; } = null!;
}

public sealed class GccV2ProductSchema : GccV2OwnedCatalog
{
    public List<GccV2ProductSchemaVersion> Versions { get; set; } = [];
}

public sealed class GccV2ProductSchemaVersion : GccV2GovernedVersion
{
    public Guid ProductSchemaId { get; set; }
    public string FieldsJson { get; set; } = "[]";
    [JsonIgnore] public GccV2ProductSchema ProductSchema { get; set; } = null!;
}

public sealed class GccV2Product : GccV2OwnedCatalog
{
    public List<GccV2ProductVersion> Versions { get; set; } = [];
}

public sealed class GccV2ProductVersion : GccV2GovernedVersion
{
    public Guid ProductId { get; set; }
    public Guid ProductSchemaVersionId { get; set; }
    public string FieldValuesJson { get; set; } = "{}";
    public string AttributeProvenanceJson { get; set; } = "{}";
    public string ApprovedClaimsJson { get; set; } = "[]";
    public string ProhibitedClaimsJson { get; set; } = "[]";
    public string MandatoryDisclaimersJson { get; set; } = "[]";
    [JsonIgnore] public GccV2Product Product { get; set; } = null!;
}

public sealed class GccV2RunAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid CreateId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string SafeFileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public long ByteSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string IngestionState { get; set; } = "upload_pending";
    public DateTimeOffset UploadExpiresAtUtc { get; set; }
    public DateTimeOffset RetainUntilUtc { get; set; }
    public DateTimeOffset? FinalizedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GccV2ContextSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CreateId { get; set; }
    public string SelectionJson { get; set; } = "{}";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GccV2ContextIngestionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Status { get; set; } = "queued";
    public int ProgressPercent { get; set; }
    public int AttemptCount { get; set; }
    public string? ClaimedByInstanceId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset? HeartbeatAtUtc { get; set; }
    public string? TerminalError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<GccV2ContextIngestionEvent> Events { get; set; } = [];
}

public sealed class GccV2ContextIngestionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public int Seq { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2ContextIngestionJob Job { get; set; } = null!;
}

public sealed class GccV2RunContextManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public int Attempt { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CanonicalJson { get; set; } = "{}";
    public string Sha256 { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string SigningKeyId { get; set; } = string.Empty;
    public string ResolverIdentity { get; set; } = string.Empty;
    public DateTimeOffset ResolvedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReplacesManifestId { get; set; }
    public List<GccV2RunContextManifestEntry> Entries { get; set; } = [];
}

public sealed class GccV2RunContextManifestEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ManifestId { get; set; }
    public string ContextKind { get; set; } = string.Empty;
    public Guid StableId { get; set; }
    public Guid? VersionId { get; set; }
    public int? VersionNumber { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string LifecycleDecision { get; set; } = string.Empty;
    public string PermissionDecision { get; set; } = string.Empty;
    public string FreshnessDecision { get; set; } = string.Empty;
    public string SelectionSource { get; set; } = "run_override";
    public string? SelectedFieldIdsJson { get; set; }
    public DateTimeOffset? SourceModifiedAtUtc { get; set; }
    [JsonIgnore] public GccV2RunContextManifest Manifest { get; set; } = null!;
}

public sealed class GccV2ContextFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Severity { get; set; } = "warning";
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Disposition { get; set; } = "open";
    public string? ReviewerUserId { get; set; }
    public string? ReviewerRationale { get; set; }
    public uint Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisposedAtUtc { get; set; }
}

public sealed class GccV2ContextAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public Guid? VersionId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string DetailJson { get; set; } = "{}";
    public string? RequestId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
