using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ISerpAnalysisService
{
    Task<Result<DeepSerpResult>> AnalyzeAsync(Guid userId, DeepSerpRequest request, CancellationToken ct = default);
}
