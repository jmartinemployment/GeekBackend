namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Ad-copy few-shot template shared by the RAG writer (<c>/rag</c>) and any task agent that
/// wants ad-template grounding — see plans/make-content-creator-workable.md Milestone 1.
/// Replaces <c>localStorage</c>-only templates (previously seeded with three generic B2B
/// samples), which vanished per-browser and could not be shared across sessions.
/// </summary>
public class GccV2AdTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string? Framework { get; set; }
    public string Body { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAtUtc { get; set; }
}
