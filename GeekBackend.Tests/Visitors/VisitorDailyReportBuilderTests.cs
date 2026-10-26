using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorDailyReportBuilderTests
{
    [Fact]
    public void A_normal_new_york_day_is_twenty_four_hours_starting_at_local_midnight()
    {
        var (start, end) = VisitorClock.DayWindowUtc(new DateOnly(2026, 10, 17));
        Assert.Equal(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public void The_spring_forward_day_is_twenty_three_hours()
    {
        var (start, end) = VisitorClock.DayWindowUtc(new DateOnly(2026, 3, 8));
        Assert.Equal(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(TimeSpan.FromHours(23), end - start);
    }

    [Fact]
    public void The_fall_back_day_is_twenty_five_hours()
    {
        var (start, end) = VisitorClock.DayWindowUtc(new DateOnly(2026, 11, 1));
        Assert.Equal(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(TimeSpan.FromHours(25), end - start);
    }

    [Fact]
    public void Day_keys_follow_new_york_not_utc()
    {
        // 03:30 UTC on the 18th is still the evening of the 17th in New York.
        Assert.Equal("2026-10-17", VisitorClock.DayKey(new DateTime(2026, 10, 18, 3, 30, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-10-18", VisitorClock.DayKey(new DateTime(2026, 10, 18, 4, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Totals_count_visitors_sessions_pages_florida_identified_intent_and_drops()
    {
        var day = new DateOnly(2026, 10, 17);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var sessions = new List<VisitorSession>
        {
            Session(a, 1, day, 10, 20, florida: true, score: 65, intent: true, pages: 3),
            Session(a, 2, day, 15, 60, florida: true, score: 20, pages: 1),
            Session(b, 4, day, 16, 100, florida: false, score: 5, pages: 2),
        };
        var profiles = new List<VisitorProfile>
        {
            new() { Id = a, SessionCount = 2, FirstSeenAt = DateTime.UtcNow, Identity = new VisitorIdentity { FirstName = "Jane", LastName = "Smith", Email = "j@example.com", Phone = "5615550100", Source = "booking", IdentifiedAt = DateTime.UtcNow } },
            new() { Id = b, SessionCount = 4, FirstSeenAt = DateTime.UtcNow },
        };
        var drops = new VisitorFilterDay { Id = "2026-10-17", Counts = new Dictionary<string, int> { ["bot"] = 31, ["gpc"] = 2 } };

        var report = VisitorDailyReportBuilder.Build(day, sessions, profiles, drops, VisitorDailyReportBuilder.VisitorScores(sessions));

        Assert.Equal(2, report.Totals.Visitors);
        Assert.Equal(1, report.Totals.NewVisitors);
        Assert.Equal(1, report.Totals.ReturningVisitors);
        Assert.Equal(3, report.Totals.Sessions);
        Assert.Equal(6, report.Totals.PageViews);
        Assert.Equal(60, report.Totals.MedianActiveSeconds);
        Assert.Equal(2, report.Totals.FloridaSessions);
        Assert.Equal(1, report.Totals.IdentifiedVisitors);
        Assert.Equal(1, report.Totals.IntentSessions);
        Assert.Equal(31, report.Totals.Drops["bot"]);
        Assert.Equal("Website visitors, Sat 17 Oct 2026: 2 visitors, 3 sessions, 1 identified", report.Title);
        Assert.Contains("Intent sessions (Florida, score 60 or more): 1", report.Body);
        Assert.Contains("1. Score 65", report.Body);
        Assert.Contains("; intent reached", report.Body);
        Assert.DoesNotContain("NOTHING ARRIVED", report.Body);
        Assert.Contains("Jane Smith (j@example.com, 5615550100)", report.Body);
        Assert.Contains("30-day score 85", report.Body);
        Assert.Contains("Dropped requests: bot 31, gpc 2", report.Body);
    }

    [Fact]
    public void Top_lists_are_ordered_by_count_then_name_and_hold_ten()
    {
        var day = new DateOnly(2026, 10, 17);
        var sessions = new List<VisitorSession>();
        for (var i = 0; i < 12; i++)
        {
            var count = 12 - i;
            for (var n = 0; n < count; n++)
            {
                var s = Session(Guid.NewGuid(), 1, day, 10, 5, false, 1, pages: 1);
                s.Pages[0].Path = $"/page-{i:00}";
                sessions.Add(s);
            }
        }
        var report = VisitorDailyReportBuilder.Build(day, sessions, Array.Empty<VisitorProfile>(), null, new Dictionary<Guid, double>());
        var block = report.Body[report.Body.IndexOf("TOP LANDING PAGES", StringComparison.Ordinal)..report.Body.IndexOf("TOP REFERRERS", StringComparison.Ordinal)];
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.Equal(10, lines.Count);
        Assert.Contains("12  /page-00", lines[0]);
        Assert.Contains("3  /page-09", lines[9]);
        Assert.DoesNotContain("/page-10", block);
    }

    [Fact]
    public void An_empty_day_still_builds_a_report_with_zero_totals()
    {
        var report = VisitorDailyReportBuilder.Build(new DateOnly(2026, 10, 17), Array.Empty<VisitorSession>(), Array.Empty<VisitorProfile>(), null, new Dictionary<Guid, double>());
        Assert.Equal("Website visitors, Sat 17 Oct 2026: 0 visitors, 0 sessions, 0 identified", report.Title);
        Assert.Contains("NOTHING ARRIVED", report.Body);
        Assert.Contains("the site did not pass its visits on", report.Body);
        Assert.Contains("No sessions were recorded.", report.Body);
        Assert.Contains("Dropped requests: none", report.Body);
        Assert.Equal(0, report.Totals.Sessions);
        Assert.NotEmpty(report.SessionsCsv);
    }

    [Fact]
    public void The_body_stops_at_one_hundred_sessions_and_the_csv_holds_them_all()
    {
        var day = new DateOnly(2026, 10, 17);
        var sessions = Enumerable.Range(0, 130)
            .Select(i => Session(Guid.NewGuid(), 1, day, 8, 5, false, i, pages: 1))
            .ToList();
        var report = VisitorDailyReportBuilder.Build(day, sessions, Array.Empty<VisitorProfile>(), null, new Dictionary<Guid, double>());
        Assert.Contains("130 in all, 100 shown", report.Body);
        Assert.Contains("\n100. Score", report.Body);
        Assert.DoesNotContain("\n101. Score", report.Body);
        var rows = System.Text.Encoding.UTF8.GetString(report.SessionsCsv).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(131, rows.Length);
    }

    private static VisitorSession Session(Guid visitorId, int visitNumber, DateOnly day, int hourNewYork, int activeSeconds, bool florida, double score, bool intent = false, int pages = 1)
    {
        var (start, _) = VisitorClock.DayWindowUtc(day);
        var startedAt = start.AddHours(hourNewYork);
        var s = new VisitorSession
        {
            Id = Guid.NewGuid(),
            VisitorId = visitorId,
            VisitNumber = visitNumber,
            StartedAt = startedAt,
            LastSeenAt = startedAt.AddSeconds(activeSeconds),
            Ip = "203.0.113.1",
            Geo = new VisitorGeo { Country = "US", Region = florida ? "FL" : "GA", City = florida ? "Boca Raton" : "Atlanta", Timezone = "America/New_York" },
            Florida = florida,
            UserAgent = new VisitorUserAgent { Raw = "ua", Browser = "Chrome", DeviceType = "desktop", Os = "Windows" },
            Score = score,
            Pages = Enumerable.Range(0, pages).Select(i => new VisitorPageView
            {
                PageViewId = $"pv{i}",
                Path = i == 0 ? "/" : $"/use-cases/{i}",
                EnteredAt = startedAt.AddSeconds(i),
                ActiveMs = i == 0 ? activeSeconds * 1000L : 0,
            }).ToList(),
        };
        if (intent) s.IntentReachedAt = startedAt.AddSeconds(2);
        return s;
    }

    [Fact]
    public void Nothing_arrived_means_no_sessions_and_no_drops_at_all()
    {
        Assert.True(VisitorDailyReportBuilder.NothingArrived(new VisitorReportTotals()));
        Assert.False(VisitorDailyReportBuilder.NothingArrived(new VisitorReportTotals { Drops = { ["bot"] = 1 } }));
        Assert.False(VisitorDailyReportBuilder.NothingArrived(new VisitorReportTotals { Sessions = 1 }));
        Assert.True(VisitorDailyReportBuilder.NothingArrived(new VisitorReportTotals { Drops = { ["bot"] = 0 } }));
    }
}
