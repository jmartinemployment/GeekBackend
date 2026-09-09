using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>Owner-scoped durable canvas project (multi-asset workspace).</summary>
public sealed class GccV2CanvasProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>planning | in-progress | review | complete</summary>
    public string Status { get; set; } = "planning";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ActivityJson { get; set; } = "[]";
    public List<GccV2CanvasAsset> Assets { get; set; } = [];
}

/// <summary>Asset node within a canvas project; lineage via <see cref="ParentAssetIdsJson"/>.</summary>
public sealed class GccV2CanvasAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>brief | article | social | image | email</summary>
    public string Kind { get; set; } = "brief";
    public string ParentAssetIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2CanvasProject Project { get; set; } = null!;
    public List<GccV2CanvasAssetVersion> Versions { get; set; } = [];
}

/// <summary>Append-only immutable version row for a canvas asset.</summary>
public sealed class GccV2CanvasAssetVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>draft | in-review | approved | published</summary>
    public string Status { get; set; } = "draft";
    public string Summary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "[]";
    public string ProvenanceJson { get; set; } = "{}";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2CanvasAsset Asset { get; set; } = null!;
}
