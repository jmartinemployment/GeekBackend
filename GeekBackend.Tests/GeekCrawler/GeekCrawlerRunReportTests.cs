using GeekAPI.Services.GeekCrawler;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// Excluded is not failed. A crawl obeying robots.txt is a crawl working correctly, and counting
/// those pages as errors makes a healthy crawl look broken while burying the real failures.
/// </summary>
public sealed class GeekCrawlerRunReportTests
{
    [Fact]
    public void Policy_exclusions_do_not_count_as_failures()
    {
        var report = new GeekCrawlerRunReport
        {
            PagesStored = 100,
            ExcludedByPolicy = new GeekCrawlerExcludedByPolicy
            {
                RobotsDisallowed = 40,
                LocaleExcluded = 10,
            },
        };

        Assert.Equal(0, report.TotalFailed);
        Assert.Equal(50, report.TotalExcluded);

        // 100 stored, nothing failed: a clean crawl, however many pages policy excluded.
        Assert.Equal(0d, report.FailureRate);
    }

    [Fact]
    public void Failure_rate_measures_what_was_attempted_not_what_was_excluded()
    {
        var report = new GeekCrawlerRunReport
        {
            PagesStored = 25,
            ExcludedByPolicy = new GeekCrawlerExcludedByPolicy { RobotsDisallowed = 500 },
            Failed = new GeekCrawlerFailureBreakdown { RequestFailed = 75 },
        };

        // 75 of 100 attempted failed. The 500 excluded pages were never attempted and must not
        // dilute the rate into looking healthy.
        Assert.Equal(0.75d, report.FailureRate);
    }

    [Fact]
    public void Challenge_pages_are_counted_apart_from_request_failures()
    {
        // A wave of these means the crawl identity is being rejected -- retrying cannot fix it,
        // which is why it must not be averaged into generic request failures.
        var report = new GeekCrawlerRunReport
        {
            PagesStored = 0,
            Failed = new GeekCrawlerFailureBreakdown { ChallengePage = 500, RequestFailed = 2 },
        };

        Assert.Equal(502, report.TotalFailed);
        Assert.Equal(500, report.Failed.ChallengePage);
        Assert.Equal(1d, report.FailureRate);
    }

    [Fact]
    public void A_report_that_will_not_parse_is_absent_not_empty()
    {
        // "No report" and "a crawl that lost nothing" must never look the same.
        Assert.Null(GeekCrawlerRunReport.FromJson("{not json"));
        Assert.Null(GeekCrawlerRunReport.FromJson(null));
        Assert.Null(GeekCrawlerRunReport.FromJson("   "));
    }

    [Fact]
    public void Report_survives_a_json_roundtrip()
    {
        var original = new GeekCrawlerRunReport
        {
            PagesStored = 1200,
            LinksStored = 98_000,
            ExcludedByPolicy = new GeekCrawlerExcludedByPolicy { RobotsDisallowed = 3, LocaleExcluded = 17 },
            Failed = new GeekCrawlerFailureBreakdown { RequestFailed = 9, ChallengePage = 4, ExtractEmpty = 2 },
            StatusCounts = new Dictionary<string, int>(StringComparer.Ordinal) { ["200"] = 1200, ["403"] = 4 },
            Samples = [new GeekCrawlerFailureSample("challenge_page", "https://example.test/pricing", "cf challenge")],
        };

        var parsed = GeekCrawlerRunReport.FromJson(original.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(1200, parsed!.PagesStored);
        Assert.Equal(3, parsed.ExcludedByPolicy.RobotsDisallowed);
        Assert.Equal(4, parsed.Failed.ChallengePage);
        Assert.Equal(4, parsed.StatusCounts["403"]);
        Assert.Equal("challenge_page", parsed.Samples[0].Reason);
    }
}
