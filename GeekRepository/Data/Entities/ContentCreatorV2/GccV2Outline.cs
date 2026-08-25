namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// A versioned outline for a brief. <see cref="HierarchyChildHeadingsJson"/> carries the real
/// sub-heading hierarchy (Phase 5 OverlapGate input) — separate column so it can be diffed/patched
/// independently of the rest of the outline shape. Frozen once WRITE begins.
/// </summary>
public class GccV2Outline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BriefId { get; set; }
    public int Version { get; set; } = 1;
    public string OutlineJson { get; set; } = "{}";
    public string HierarchyChildHeadingsJson { get; set; } = "[]";
    public DateTimeOffset? FrozenAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
