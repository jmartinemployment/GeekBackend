namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>A versioned content brief for a create. Frozen once WRITE begins.</summary>
public class GccV2Brief
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }
    public int Version { get; set; } = 1;
    public string TargetKeyword { get; set; } = string.Empty;
    public string ContentType { get; set; } = "blog";
    public string RawBriefJson { get; set; } = "{}";
    public DateTimeOffset? FrozenAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
