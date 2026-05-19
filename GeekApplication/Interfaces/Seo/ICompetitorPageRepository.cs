using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ICompetitorPageRepository
{
    Task<Result<IReadOnlyList<SeoCompetitorPage>>> GetBySerpResultAsync(Guid serpResultId, CancellationToken ct = default);

    Task<Result<SeoCompetitorPage>> UpsertAsync(
        Guid serpResultId, PageContent page, CancellationToken ct = default);
}
