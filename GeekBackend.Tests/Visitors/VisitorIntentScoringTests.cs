using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorIntentScoringTests
{
    [Theory]
    [InlineData("/proposals/acme", 50)]
    [InlineData("/proposals", 50)]
    [InlineData("/pricing", 40)]
    [InlineData("/pricing-plans", 40)]
    [InlineData("/architectural-infrastructure-audit", 30)]
    [InlineData("/use-cases/marketing/ad-spend-optimization", 10)]
    [InlineData("/tools/accounting/x", 8)]
    [InlineData("/methodology", 8)]
    [InlineData("/blog/marketing/post", 3)]
    [InlineData("/blogger", 0)]
    [InlineData("/glossary/term", 2)]
    [InlineData("/", 2)]
    [InlineData("/privacy-policy", 0)]
    [InlineData("/terms-and-conditions", 0)]
    [InlineData("/Use-Cases/", 10)]
    public void Path_weights(string path, double expected) =>
        Assert.Equal(expected, VisitorIntentScoring.PathWeight(path));

    [Fact]
    public void Time_factor_runs_from_a_quarter_at_zero_seconds_to_full_at_120()
    {
        Assert.Equal(2.5, VisitorIntentScoring.Score(Session(("/use-cases", 0))));
        Assert.Equal(10, VisitorIntentScoring.Score(Session(("/use-cases", 120_000))));
        Assert.Equal(10, VisitorIntentScoring.Score(Session(("/use-cases", 600_000))));
        Assert.Equal(6.3, VisitorIntentScoring.Score(Session(("/use-cases", 60_000))));
    }

    [Fact]
    public void Repeat_views_of_a_path_sum_their_seconds_before_the_cap()
    {
        var once = VisitorIntentScoring.Score(Session(("/use-cases", 120_000)));
        var twice = VisitorIntentScoring.Score(Session(("/use-cases", 60_000), ("/use-cases", 60_000)));
        Assert.Equal(once, twice);
        Assert.Equal(10, twice);
    }

    [Fact]
    public void Distinct_page_bonus_is_five_each_after_the_first_and_caps_at_twenty()
    {
        // /use-cases at 0 s is 2.5, /tools at 0 s is 2, and the second distinct page adds 5.
        Assert.Equal(9.5, VisitorIntentScoring.Score(Session(("/use-cases", 0), ("/tools", 0))));
        var six = Session(("/a", 0), ("/b", 0), ("/c", 0), ("/d", 0), ("/e", 0), ("/f", 0));
        Assert.Equal(20, VisitorIntentScoring.Score(six));
    }

    [Fact]
    public void Scheduler_view_adds_twenty_five_once()
    {
        var s = Session(("/", 0));
        s.Scheduler = new VisitorSchedulerFunnel { ViewedAt = DateTime.UtcNow, DatePickedAt = DateTime.UtcNow };
        Assert.Equal(0.5 + 25, VisitorIntentScoring.Score(s));
    }

    [Fact]
    public void Total_caps_at_one_hundred()
    {
        var s = Session(("/proposals/a", 120_000), ("/pricing", 120_000), ("/architectural-infrastructure-audit", 120_000));
        s.Scheduler = new VisitorSchedulerFunnel { ViewedAt = DateTime.UtcNow };
        Assert.Equal(100, VisitorIntentScoring.Score(s));
    }

    [Fact]
    public void Threshold_is_sixty_inclusive()
    {
        Assert.False(VisitorIntentScoring.ReachesIntent(59.9));
        Assert.True(VisitorIntentScoring.ReachesIntent(60));
    }

    [Fact]
    public void A_score_never_decreases_as_active_time_grows()
    {
        double last = -1;
        for (var ms = 0; ms <= 200_000; ms += 10_000)
        {
            var score = VisitorIntentScoring.Score(Session(("/use-cases/x", ms), ("/tools/y", ms)));
            Assert.True(score >= last, $"{score} < {last} at {ms} ms");
            last = score;
        }
    }

    [Fact]
    public void Florida_box_holds_delray_beach_and_not_atlanta()
    {
        Assert.True(VisitorIntentScoring.IsInFlorida(26.46, -80.07));
        Assert.False(VisitorIntentScoring.IsInFlorida(33.75, -84.39));
    }

    private static VisitorSession Session(params (string Path, long ActiveMs)[] pages) => new()
    {
        Id = Guid.NewGuid(),
        VisitorId = Guid.NewGuid(),
        Pages = pages.Select((p, i) => new VisitorPageView { PageViewId = $"pv{i}", Path = p.Path, ActiveMs = p.ActiveMs }).ToList(),
    };
}
