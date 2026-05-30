using GeekSeo.Persistence.Entities;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ISerpCacheRepository
{
    Task<Result<SeoSerpResult?>> GetAsync(string keyword, string location, string languageCode, CancellationToken ct = default);

    Task<Result<SeoSerpResult>> UpsertAsync(
        string keyword, string location, string languageCode,
        SerpResult serp, SerpBenchmarksPayload benchmarks,
        CancellationToken ct = default);

    Task<Result> DeleteAsync(
        string keyword, string location, string languageCode,
        CancellationToken ct = default);
}
