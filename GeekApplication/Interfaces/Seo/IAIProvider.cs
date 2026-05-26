using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IAIProvider
{
    string ProviderName { get; }
    Task<Result<AIResponse>> CompleteAsync(AIRequest request, CancellationToken ct = default);
}
