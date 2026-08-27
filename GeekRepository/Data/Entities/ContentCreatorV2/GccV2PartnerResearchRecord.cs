namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Audit + cache row for one polite partner-destination crawl (geekatyourspotbot).
/// Successful <see cref="PageJson"/> may be reused within 24h without re-fetching.
/// </summary>
public class GccV2PartnerResearchRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }
    public Guid? JobId { get; set; }

    public string TargetUrl { get; set; } = string.Empty;
    public string HostDomain { get; set; } = string.Empty;
    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsSuccess { get; set; }
    public string CrawlStatusLog { get; set; } = string.Empty;

    public string? ExtractedTitle { get; set; }
    /// <summary>Serialized <c>GccQuoteablePage</c> when successful — used for WRITE + 24h cache.</summary>
    public string? PageJson { get; set; }
    /// <summary>Lean flattened text for audit; no raw HTML blob.</summary>
    public string? FlattenedTextContent { get; set; }
}
