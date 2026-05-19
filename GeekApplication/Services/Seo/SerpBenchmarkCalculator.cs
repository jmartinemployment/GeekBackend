using GeekApplication.Models.Seo;

namespace GeekApplication.Services.Seo;

public static class SerpBenchmarkCalculator
{
    public static SerpBenchmarksPayload FromSerp(SerpResult serp)
    {
        var organic = serp.OrganicResults.Take(10).ToList();
        var snippetWordCounts = organic
            .Select(o => CountWords(o.Snippet))
            .Where(w => w > 0)
            .ToList();

        var avgSnippet = snippetWordCounts.Count > 0 ? (int)snippetWordCounts.Average() : 40;
        var avgWordCount = Math.Max(800, avgSnippet * 12);

        var avgTitleLength = organic.Count > 0
            ? (int)organic.Average(o => o.Title.Length)
            : 55;

        return new SerpBenchmarksPayload
        {
            AvgWordCount = avgWordCount,
            AvgTitleLength = avgTitleLength,
            BenchmarkQuality = "low_sample_count",
            TopDomains = organic.Select(o => o.Domain ?? string.Empty).Where(d => d.Length > 0).ToList(),
            OrganicResults = organic,
        };
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
