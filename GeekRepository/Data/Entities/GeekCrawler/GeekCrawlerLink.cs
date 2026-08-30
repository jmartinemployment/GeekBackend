namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid PageId { get; set; }
    public string FromUrl { get; set; } = string.Empty;
    public string LinkUrl { get; set; } = string.Empty;
    public bool IsSameOrigin { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
