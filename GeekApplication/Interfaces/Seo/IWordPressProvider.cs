using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IWordPressProvider
{
    string ProviderName { get; }

    Task<Result<WordPressConnectionTestResult>> TestConnectionAsync(
        WordPressCredentials credentials, CancellationToken ct = default);

    Task<Result<WordPressPublishProviderResult>> PublishPostAsync(
        WordPressCredentials credentials, WordPressPostPayload post, CancellationToken ct = default);
}
