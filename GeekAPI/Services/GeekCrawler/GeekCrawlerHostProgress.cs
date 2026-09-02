namespace GeekAPI.Services.GeekCrawler;

public sealed class OriginProgressStats
{
    public int Attempted { get; set; }
    public int WithHtml { get; set; }
    public string? LastFailureReason { get; set; }
    public int? PagesInQueue { get; set; }
    public int? InFlightCount { get; set; }
    public double? PagesPerMinute { get; set; }
    public Dictionary<string, int> StatusCounts { get; } = new(StringComparer.Ordinal);

    public void AddPage(int statusCode, bool hasHtml, string? failureReason = null)
    {
        Attempted++;
        if (hasHtml)
            WithHtml++;
        else if (!string.IsNullOrWhiteSpace(failureReason))
            LastFailureReason = failureReason;

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
                pagesInQueue = s.PagesInQueue,
                inFlightCount = s.InFlightCount,
                pagesPerMinute = s.PagesPerMinute,
                statusCounts = s.StatusCounts.Count > 0
                    ? s.StatusCounts
                    : null,
                lastFailureReason = s.LastFailureReason,
            };
        }).ToList();
    }

    public static bool AllOriginsHaveZeroHtml(IReadOnlyDictionary<string, OriginProgressStats> originStats) =>
        originStats.Values.All(s => s.WithHtml == 0);

    /// <summary>Distinguishes all status-0 failures from empty 2xx responses.</summary>
    public static string DescribeZeroHtmlFailure(IReadOnlyDictionary<string, OriginProgressStats> originStats)
    {
        if (originStats.Values.All(s => s.Attempted == 0))
            return "Crawl finished with no HTML — no pages were attempted.";

        if (originStats.Values.All(s => s is { WithHtml: 0, Attempted: > 0 }
                                         && s.StatusCounts.Count == 1
                                         && s.StatusCounts.ContainsKey("0")))
        {
            return "Crawl finished with no HTML — all page fetches failed (status 0).";
        }

        if (originStats.Values.All(s => s is { WithHtml: 0, Attempted: > 0 }
                                         && !s.StatusCounts.ContainsKey("0")))
        {
            return "Crawl finished with no HTML — pages returned 2xx but empty responses.";
        }

        return "Crawl finished with no HTML on any page.";
    }

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
