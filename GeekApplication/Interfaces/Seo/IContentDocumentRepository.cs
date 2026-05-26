using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IContentDocumentRepository
{
    Task<Result<SeoContentDocument>> GetByIdAsync(Guid documentId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeoContentDocument>>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> CreateAsync(Guid userId, CreateContentDocumentRequest request, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> UpdateContentAsync(Guid documentId, UpdateContentRequest request, int wordCount, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> UpdateStatusAsync(Guid documentId, string status, CancellationToken ct = default);
    Task<Result> UpdateScoreAsync(Guid documentId, int score, string scoreComponentsJson, CancellationToken ct = default);
    Task<Result> UpdateAiDetectionScoreAsync(Guid documentId, decimal score, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid documentId, CancellationToken ct = default);
}
