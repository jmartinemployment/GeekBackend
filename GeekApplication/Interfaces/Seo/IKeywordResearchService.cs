using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IKeywordResearchService
{
    Task<Result<IReadOnlyList<KeywordResult>>> ResearchAsync(
        Guid userId, KeywordResearchRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<KeywordCluster>>> ClusterAsync(
        Guid userId, ClusterKeywordsRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<SeoKeywordDto>>> GetProjectKeywordsAsync(
        Guid userId, Guid projectId, CancellationToken ct = default);
}

public sealed record SeoKeywordDto
{
    public required Guid Id { get; init; }
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public int? SearchVolume { get; init; }
    public decimal? KeywordDifficulty { get; init; }
    public string? Intent { get; init; }
    public DateTimeOffset? CachedAt { get; init; }
}
