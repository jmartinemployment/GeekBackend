using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ISerpProvider
{
    Task<Result<SerpResult>> GetSerpResultsAsync(SerpRequest request, CancellationToken ct = default);
    string ProviderName { get; }
}
