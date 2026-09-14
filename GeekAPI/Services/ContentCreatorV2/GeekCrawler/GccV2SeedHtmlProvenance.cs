using System.Security.Cryptography;
using System.Text;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

/// <summary>
/// K2 contracts: stamp seed-HTML / rag-chunk provenance and fail closed on unauthorized runs.
/// </summary>
public static class GccV2SeedHtmlProvenance
{
    public static string DigestUtf8(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static GccQuoteablePage StampSeedHtml(
        GccQuoteablePage page,
        Guid runId,
        Guid pageId,
        string? sourceHtml,
        DateTimeOffset? crawledAtUtc)
    {
        var digest = !string.IsNullOrEmpty(sourceHtml)
            ? DigestUtf8(sourceHtml)
            : DigestUtf8(string.Join("\n", page.Paragraphs));
        if (string.IsNullOrEmpty(digest))
            throw new InvalidOperationException(
                "Seed HTML extract produced an empty body digest — re-crawl the project site or wait for index.");

        return page with
        {
            PageId = pageId.ToString("D"),
            RunId = runId.ToString("D"),
            RetrievalMode = GccQuoteablePage.RetrievalModeSeedHtml,
            SourceDigest = digest,
            CrawledAtUtc = crawledAtUtc,
        };
    }

    public static GccQuoteablePage StampRagChunk(GccQuoteablePage page, Guid? runId = null) =>
        page with
        {
            RunId = runId?.ToString("D") ?? page.RunId,
            RetrievalMode = GccQuoteablePage.RetrievalModeRagChunk,
        };

    /// <summary>
    /// Foreign / unauthorized runId must not emit citations. Owner must match; empty owner fails closed.
    /// </summary>
    public static void EnsureRunAuthorized(string ownerUserId, string? runOwnerUserId, Guid runId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new UnauthorizedAccessException("Owner is required to authorize research run access.");
        if (string.IsNullOrWhiteSpace(runOwnerUserId)
            || !string.Equals(ownerUserId.Trim(), runOwnerUserId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Research run {runId:D} is not authorized for the current owner.");
        }
    }
}
