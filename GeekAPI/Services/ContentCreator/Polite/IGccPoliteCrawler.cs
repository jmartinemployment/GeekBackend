namespace GeekAPI.Services.ContentCreator.Polite;

public interface IGccPoliteCrawler
{
    /// <summary>
    /// Politely fetch HTML for <paramref name="url"/> (robots gate, per-host delay, soft-skip).
    /// </summary>
    Task<GccPoliteFetchResult> GetHtmlAsync(Uri url, CancellationToken cancellationToken = default);
}
