namespace GeekAPI.HttpClients;

public record GeekCrawlerRunDto(
    Guid Id,
    string OwnerUserId,
    string CrawlType,
    string Status,
    string SeedUrlsJson,
    string? HostProgressJson,
    string? ErrorSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public record CreateGeekCrawlerRunCommand(
    string OwnerUserId,
    string CrawlType,
    string? SeedUrlsJson);

public record PatchGeekCrawlerRunCommand(
    string? Status = null,
    string? HostProgressJson = null,
    string? ErrorSummary = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

public record GeekCrawlerPageDto(
    Guid Id,
    Guid RunId,
    string Origin,
    string Url,
    string FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html,
    DateTimeOffset CrawledAtUtc);

public record CreateGeekCrawlerPageBatchCommand(
    Guid RunId,
    IReadOnlyList<CreateGeekCrawlerPageItemCommand> Pages);

public record CreateGeekCrawlerPageItemCommand(
    string Origin,
    string Url,
    string? FinalUrl,
    int StatusCode,
    bool RobotsAllowed,
    string? Html);

public record GeekCrawlerPageBatchResult(
    int Count,
    IReadOnlyList<GeekCrawlerCreatedPageDto> Pages);

public record GeekCrawlerPageActivityDto(
    int PageCount,
    DateTimeOffset? LastCrawledAtUtc);

public record GeekCrawlerCreatedPageDto(string Url, Guid PageId);

public record CreateGeekCrawlerLinkBatchCommand(
    Guid RunId,
    IReadOnlyList<CreateGeekCrawlerLinkItemCommand> Links);

public record CreateGeekCrawlerLinkItemCommand(
    Guid PageId,
    string FromUrl,
    string LinkUrl,
    bool IsSameOrigin);

public record GeekCrawlerLinkDto(
    Guid Id,
    Guid RunId,
    Guid PageId,
    string FromUrl,
    string LinkUrl,
    bool IsSameOrigin,
    DateTimeOffset DiscoveredAtUtc);
