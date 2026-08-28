namespace GeekAPI.Services.ContentCreatorV2.Polite;

public interface IGccV2PoliteCrawler
{
    /// <summary>
    /// Politely fetch HTML for <paramref name="url"/> (robots gate, per-host delay, soft-skip).
    /// </summary>
    Task<GccV2PoliteFetchResult> GetHtmlAsync(Uri url, CancellationToken cancellationToken = default);
}
