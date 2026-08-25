namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>Durable output of one pipeline stage (optionally scoped to a section) for a job.</summary>
public class GccV2StageResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    public string OutputJson { get; set; } = "{}";
    public int TokensUsed { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
