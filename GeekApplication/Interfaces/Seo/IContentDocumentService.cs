using GeekSeo.Persistence.Entities;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IContentDocumentService
{
    Task<Result<SeoContentDocument>> EnsureAccessAsync(Guid userId, Guid documentId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeoContentDocument>>> ListByProjectAsync(Guid userId, Guid projectId, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> GetAsync(Guid userId, Guid documentId, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> CreateAsync(Guid userId, CreateContentDocumentRequest request, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> UpdateContentAsync(Guid userId, Guid documentId, UpdateContentRequest request, CancellationToken ct = default);
    Task<Result<SeoContentDocument>> UpdateStatusAsync(Guid userId, Guid documentId, string status, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid userId, Guid documentId, CancellationToken ct = default);
}
