using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ICompetitorInsightsService
{
    Task<Result<CompetitorInsightsResult>> GetForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default);

    Task<Result<CompetitorInsightsResult>> RefreshCrawlForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default);
}

public sealed record CompetitorInsightsResult
{
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public required IReadOnlyList<CompetitorPageInsight> Pages { get; init; }
    public string BenchmarkQuality { get; init; } = "good";
    public string CrawlStatus { get; init; } = "complete";
}

public sealed record CompetitorPageInsight
{
    public required string Url { get; init; }
    public string? Domain { get; init; }
    public int Position { get; init; }
    public int WordCount { get; init; }
    public string? MetaTitle { get; init; }
    public DateTimeOffset? CrawledAt { get; init; }
}
