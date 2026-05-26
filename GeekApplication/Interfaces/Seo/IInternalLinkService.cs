using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IInternalLinkService
{
    Task<Result<IReadOnlyList<InternalLinkSuggestion>>> SuggestAsync(
        Guid userId, InternalLinkSuggestRequest request, CancellationToken ct = default);
}
