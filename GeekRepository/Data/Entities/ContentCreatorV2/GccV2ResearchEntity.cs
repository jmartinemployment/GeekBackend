namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Canonical partner/competitor entity shared by the RAG writer (<c>/rag</c>) and every
/// task agent. Replaces free-text entity names and the hardcoded Phase-0 seed list
/// (<c>RagEntitySeedList</c> in GeekAPI) with an identity every RAG-grounded surface can share.
/// One company may have several indexed crawl pages — they all resolve to one entity here.
/// </summary>
public class GccV2ResearchEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>"partner" or "competitor" — the role this entity plays for retrieval weighting.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Primary site/domain used to resolve crawl pages for this entity, if known.</summary>
    public string? PrimaryUrl { get; set; }

    /// <summary>Free-form notes; never used for generation, curation context only.</summary>
    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAtUtc { get; set; }
}
