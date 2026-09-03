namespace GeekRepository.Data.Entities.GeekCrawler;

public record GeekCrawlerPageResumeRow(string Origin, string Url, bool HasHtml);

public record GeekCrawlerLinkResumeRow(string LinkUrl, DateTimeOffset DiscoveredAtUtc, Guid Id);
