using GeekApplication.Entities.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IWordPressConnectionRepository
{
    Task<Result<SeoWordPressConnection?>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<Result<SeoWordPressConnection>> UpsertAsync(SeoWordPressConnection connection, CancellationToken ct = default);

    Task<Result> DeleteByProjectAsync(Guid projectId, CancellationToken ct = default);
}
