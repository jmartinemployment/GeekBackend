namespace GeekRepository.Data.Entities.ContentCreatorV2;

public class GccV2Create
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = "blog";
    /// <summary>v1 <c>SiteSectionContext</c> JSON — required when writing for a crawled property.</summary>
    public string? SiteSectionJson { get; set; }
    /// <summary>Human-readable project site URL shown as Writing for: …</summary>
    public string? SiteUrl { get; set; }
    /// <summary>Owned project-site BFS crawl run that grounded this create.</summary>
    public Guid? ProjectSiteCrawlRunId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
