using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IContentBriefService
{
    Task<Result<ContentBrief>> GenerateBriefAsync(
        Guid userId, GenerateBriefRequest request, CancellationToken ct = default);
}
