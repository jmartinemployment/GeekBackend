namespace GeekAPI.Services.GeekCrawler;

public sealed class OriginProgressStats
{
    public int Attempted { get; set; }
    public int WithHtml { get; set; }
    public Dictionary<string, int> StatusCounts { get; } = new(StringComparer.Ordinal);

    public void AddPage(int statusCode, bool hasHtml)
    {
        Attempted++;
        if (hasHtml)
            WithHtml++;

        var key = statusCode.ToString();
        StatusCounts.TryGetValue(key, out var count);
        StatusCounts[key] = count + 1;
    }
}

public static class GeekCrawlerHostProgress
{
    public static List<object> BuildHostProgress(
        IEnumerable<string> origins,
        IReadOnlyDictionary<string, OriginProgressStats> originStats)
    {
        return origins.Select(o =>
        {
            originStats.TryGetValue(o, out var s);
            s ??= new OriginProgressStats();
            return (object)new
            {
                origin = o,
                pagesAttempted = s.Attempted,
                pagesWithHtml = s.WithHtml,
                pagesWithoutHtml = s.Attempted - s.WithHtml,
                statusCounts = s.StatusCounts.Count > 0
                    ? s.StatusCounts
                    : null,
            };
        }).ToList();
    }

    public static bool AllOriginsHaveZeroHtml(IReadOnlyDictionary<string, OriginProgressStats> originStats) =>
        originStats.Values.All(s => s.WithHtml == 0);

    public static Dictionary<string, OriginProgressStats> FromResumeStats(
        IReadOnlyDictionary<string, (int Attempted, int WithHtml)> resumeStats)
    {
        return resumeStats.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var stats = new OriginProgressStats
                {
                    Attempted = kvp.Value.Attempted,
                    WithHtml = kvp.Value.WithHtml,
                };
                if (kvp.Value.Attempted > kvp.Value.WithHtml)
                    stats.StatusCounts["0"] = kvp.Value.Attempted - kvp.Value.WithHtml;
                if (kvp.Value.WithHtml > 0)
                    stats.StatusCounts["200"] = kvp.Value.WithHtml;
                return stats;
            },
            StringComparer.OrdinalIgnoreCase);
    }
}
