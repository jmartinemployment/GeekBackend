using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IKeywordProvider
{
    string ProviderName { get; }

    Task<Result<IReadOnlyList<KeywordResult>>> GetKeywordSuggestionsAsync(
        string seedKeyword, string location, int count, CancellationToken ct = default);
}
