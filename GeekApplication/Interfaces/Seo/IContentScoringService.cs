using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IContentScoringService
{
    Task<Result<ContentScoreHubResult>> ProcessContentChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword,
        CancellationToken ct = default);

    Task<Result<ContentScoreHubResult>> ProcessKeywordChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword, string targetLocation,
        CancellationToken ct = default);

    Task<Result<AutoOptimizeResult>> AutoOptimizeAsync(
        Guid userId, Guid documentId, CancellationToken ct = default);
}
