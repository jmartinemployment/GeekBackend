using GeekSeo.Persistence.Entities;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IKeywordRepository
{
    Task<Result<IReadOnlyList<SeoKeyword>>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<Result> BulkUpsertAsync(Guid projectId, IReadOnlyList<KeywordResult> keywords, string location, CancellationToken ct = default);
}
