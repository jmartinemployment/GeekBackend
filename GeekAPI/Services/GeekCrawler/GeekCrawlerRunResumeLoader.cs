using GeekApplication.Models.GeekCrawler;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public sealed record GeekCrawlerBfsResume(
    IReadOnlySet<string> PreSeenUrlKeys,
    IReadOnlyList<string> QueueUrls);

public static class GeekCrawlerRunResumeLoader
{
    public sealed record ResumeState(
        Dictionary<string, (int Attempted, int WithHtml)> OriginStats,
        Dictionary<string, GeekCrawlerBfsResume> OriginResume);

    public static async Task<ResumeState> LoadAsync(
        IGeekCrawlerResumeRepository repo,
        Guid runId,
        IReadOnlyCollection<string> origins,
        CancellationToken ct)
    {
        var originStats = origins.ToDictionary(
            o => o,
            _ => (Attempted: 0, WithHtml: 0),
            StringComparer.OrdinalIgnoreCase);

        var seenByOrigin = origins.ToDictionary(
            o => o,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var offset = 0;
        while (true)
        {
            var pages = await repo.ListPagesForResumeAsync(runId, limit: 500, offset, ct).ConfigureAwait(false);
            if (pages.Count == 0)
                break;

            foreach (var page in pages)
            {
                var origin = page.Origin;
                if (!originStats.ContainsKey(origin))
                    originStats[origin] = (0, 0);

                var stats = originStats[origin];
                stats.Attempted++;
                if (page.HasHtml)
                    stats.WithHtml++;
                originStats[origin] = stats;

                if (seenByOrigin.TryGetValue(origin, out var seen))
                    seen.Add(GeekCrawlerUrlKeys.CrawlKey(page.Url));
            }

            offset += pages.Count;
            if (pages.Count < 500)
                break;
        }

        var queueByOrigin = origins.ToDictionary(
            o => o,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        offset = 0;
        while (true)
        {
            var links = await repo.ListLinksAsync(runId, sameOrigin: true, limit: 500, offset, ct)
                .ConfigureAwait(false);
            if (links.Count == 0)
                break;

            foreach (var link in links)
            {
                var key = GeekCrawlerUrlKeys.CrawlKey(link.LinkUrl);
                foreach (var origin in origins)
                {
                    if (!seenByOrigin.TryGetValue(origin, out var seen))
                        continue;
                    if (seen.Contains(key))
                        continue;
                    if (!Uri.TryCreate(link.LinkUrl, UriKind.Absolute, out var uri))
                        continue;
                    if (!string.Equals(uri.Host, new Uri(origin).Host, StringComparison.OrdinalIgnoreCase))
                        continue;

                    queueByOrigin[origin].Add(link.LinkUrl);
                    break;
                }
            }

            offset += links.Count;
            if (links.Count < 500)
                break;
        }

        var originResume = origins.ToDictionary(
            o => o,
            o => new GeekCrawlerBfsResume(
                seenByOrigin[o],
                queueByOrigin[o].ToList()),
            StringComparer.OrdinalIgnoreCase);

        return new ResumeState(originStats, originResume);
    }
}
