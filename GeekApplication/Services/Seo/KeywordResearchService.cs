using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class KeywordResearchService(
    IProjectRepository projects,
    IKeywordProvider keywordProvider,
    IKeywordRepository keywordRepository) : IKeywordResearchService
{
    public async Task<Result<IReadOnlyList<KeywordResult>>> ResearchAsync(
        Guid userId, KeywordResearchRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<IReadOnlyList<KeywordResult>>.Failure("Access denied");

        var location = string.IsNullOrWhiteSpace(request.Location)
            ? project.Value.DefaultLocation
            : request.Location;

        var count = Math.Clamp(request.ResultCount, 5, 100);
        var suggestions = await keywordProvider.GetKeywordSuggestionsAsync(
            request.SeedKeyword, location, count, ct);
        if (!suggestions.IsSuccess)
            return Result<IReadOnlyList<KeywordResult>>.Failure(suggestions.Error ?? "Keyword research failed");

        var cached = await keywordRepository.BulkUpsertAsync(
            request.ProjectId, suggestions.Value ?? [], location, ct);
        if (!cached.IsSuccess)
            return Result<IReadOnlyList<KeywordResult>>.Failure(cached.Error ?? "Failed to cache keywords");

        return Result<IReadOnlyList<KeywordResult>>.Success(suggestions.Value);
    }

    public async Task<Result<IReadOnlyList<KeywordCluster>>> ClusterAsync(
        Guid userId, ClusterKeywordsRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<IReadOnlyList<KeywordCluster>>.Failure("Access denied");

        var keywords = request.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keywords.Count == 0)
            return Result<IReadOnlyList<KeywordCluster>>.Success([]);

        var cached = await keywordRepository.GetByProjectAsync(request.ProjectId, ct);
        var metrics = (cached.Value ?? []).ToDictionary(k => k.Keyword, StringComparer.OrdinalIgnoreCase);

        var groups = keywords
            .GroupBy(k => k.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? k)
            .Select(g =>
            {
                var volumes = g.Select(k => metrics.TryGetValue(k, out var m) ? m.SearchVolume ?? 0 : 0).ToList();
                var diffs = g.Select(k => metrics.TryGetValue(k, out var m) ? (double)(m.KeywordDifficulty ?? 0) : 0).ToList();
                var pillar = g.OrderByDescending(k => metrics.TryGetValue(k, out var m) ? m.SearchVolume ?? 0 : 0).First();
                var clusterLabel = string.IsNullOrEmpty(g.Key) ? "Other" : char.ToUpper(g.Key[0]) + g.Key[1..];
                return new KeywordCluster
                {
                    ClusterName = clusterLabel,
                    PillarKeyword = pillar,
                    Keywords = g.ToList(),
                    AverageVolume = volumes.Count > 0 ? volumes.Average() : 0,
                    AverageDifficulty = diffs.Count > 0 ? diffs.Average() : 0,
                };
            })
            .ToList();

        return Result<IReadOnlyList<KeywordCluster>>.Success(groups);
    }

    public async Task<Result<IReadOnlyList<SeoKeywordDto>>> GetProjectKeywordsAsync(
        Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<IReadOnlyList<SeoKeywordDto>>.Failure("Access denied");

        var rows = await keywordRepository.GetByProjectAsync(projectId, ct);
        if (!rows.IsSuccess)
            return Result<IReadOnlyList<SeoKeywordDto>>.Failure(rows.Error ?? "Failed to load keywords");

        var dtos = (rows.Value ?? []).Select(k => new SeoKeywordDto
        {
            Id = k.Id,
            Keyword = k.Keyword,
            Location = k.Location,
            SearchVolume = k.SearchVolume,
            KeywordDifficulty = k.KeywordDifficulty,
            Intent = k.Intent,
            CachedAt = k.CachedAt,
        }).ToList();

        return Result<IReadOnlyList<SeoKeywordDto>>.Success(dtos);
    }
}
