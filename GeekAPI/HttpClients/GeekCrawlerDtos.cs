namespace GeekAPI.HttpClients;

public record GeekCrawlerRunDto(
    Guid Id,
    string OwnerUserId,
    string CrawlType,
    string Status,
    string SeedUrlsJson,
    string? SeedKey,
    string? HostProgressJson,
    string? ErrorSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public record CreateGeekCrawlerRunCommand(
    string OwnerUserId,
    string CrawlType,
    string? SeedUrlsJson,
    string? SeedKey = null);

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
    string? FailureReason,
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
    string? Html,
    string? FailureReason = null);

public record GeekCrawlerPageBatchResult(
    int Count,
    IReadOnlyList<GeekCrawlerCreatedPageDto> Pages);

public record GeekCrawlerPageActivityDto(
    int PageCount,
    DateTimeOffset? LastCrawledAtUtc);

public record GeekCrawlerPageResumeRowDto(
    string Origin,
    string Url,
    bool HasHtml);

public record GeekCrawlerLinkActivityDto(int LinkCount);

public record GeekCrawlerLinkResumeRowDto(
    string LinkUrl,
    DateTimeOffset DiscoveredAtUtc,
    Guid Id);

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

public record GeekCrawlerScheduleDto(
    Guid Id,
    string OwnerUserId,
    string CrawlType,
    string SeedUrlsJson,
    string? SeedKey,
    int IntervalHours,
    bool Enabled,
    DateTimeOffset NextRunUtc,
    DateTimeOffset? LastStartedUtc,
    Guid? LastRunId,
    DateTimeOffset CreatedAtUtc);

public record CreateGeekCrawlerScheduleCommand(
    string OwnerUserId,
    string CrawlType,
    string SeedUrlsJson,
    string? SeedKey = null,
    int? IntervalHours = null,
    bool? Enabled = null,
    DateTimeOffset? NextRunUtc = null);

public record PatchGeekCrawlerScheduleCommand(
    bool? Enabled = null,
    int? IntervalHours = null,
    DateTimeOffset? NextRunUtc = null,
    DateTimeOffset? LastStartedUtc = null,
    Guid? LastRunId = null);

public record ClaimGeekCrawlerScheduleCommand(
    DateTimeOffset ExpectedNextRunUtc,
    DateTimeOffset NewNextRunUtc,
    DateTimeOffset? LastStartedUtc = null);
