using System.Text;
using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorReportCsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Cells_are_quoted_when_they_need_to_be(string? value, string expected) =>
        Assert.Equal(expected, VisitorReportCsv.Quote(value));

    [Fact]
    public void Every_csv_starts_with_the_byte_order_mark_and_its_header_row()
    {
        var sessions = new[] { Sample() };
        var profiles = new Dictionary<Guid, VisitorProfile>();
        var scores = new Dictionary<Guid, double>();

        foreach (var (bytes, columns) in new[]
        {
            (VisitorReportCsv.Sessions(sessions, profiles, scores), VisitorReportCsv.SessionColumns),
            (VisitorReportCsv.Pages(sessions), VisitorReportCsv.PageColumns),
            (VisitorReportCsv.Interactions(sessions), VisitorReportCsv.InteractionColumns),
        })
        {
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            var lines = text.Split("\r\n");
            Assert.Equal(string.Join(',', columns), lines[0]);
        }
    }

    [Fact]
    public void Column_lists_stay_where_a_spreadsheet_expects_them()
    {
        Assert.Equal("sessionId", VisitorReportCsv.SessionColumns[0]);
        Assert.Equal("visitorScore30d", VisitorReportCsv.SessionColumns[^1]);
        Assert.Equal(97, VisitorReportCsv.SessionColumns.Count);
        Assert.Contains("intentReachedAt", VisitorReportCsv.SessionColumns);
        Assert.DoesNotContain("intentClaimedAt", VisitorReportCsv.SessionColumns);
        Assert.DoesNotContain("intentSentAt", VisitorReportCsv.SessionColumns);
        Assert.DoesNotContain("intentEmailId", VisitorReportCsv.SessionColumns);
        Assert.Equal(new[] { "sessionId", "visitorId", "pageViewId", "path", "query", "title", "enteredAt", "activeMs", "maxScrollPct", "clicks", "lcp", "inp", "cls", "fcp", "ttfb" }, VisitorReportCsv.PageColumns);
        Assert.Equal(new[] { "sessionId", "visitorId", "kind", "at", "path", "href", "linkId", "outbound" }, VisitorReportCsv.InteractionColumns);
    }

    [Fact]
    public void A_session_row_has_as_many_cells_as_the_header_and_a_quoted_title_survives()
    {
        var session = Sample();
        session.Pages[0].Title = "Budget, \"Ad Copy\"";
        var bytes = VisitorReportCsv.Pages(new[] { session });
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Budget, \"\"Ad Copy\"\"\"", lines[1]);

        var sessionsCsv = Encoding.UTF8.GetString(VisitorReportCsv.Sessions(new[] { session }, new Dictionary<Guid, VisitorProfile>(), new Dictionary<Guid, double>()));
        var row = sessionsCsv.Split("\r\n")[1];
        Assert.Equal(VisitorReportCsv.SessionColumns.Count, row.Split(',').Length);
    }

    [Fact]
    public void Interactions_list_clicks_and_funnel_steps_in_time_order()
    {
        var session = Sample();
        var t0 = session.StartedAt;
        session.Pages[0].Clicks.Add(new VisitorPageClick { Href = "/tools", LinkId = "gys-link-tools", At = t0.AddSeconds(30) });
        session.Scheduler = new VisitorSchedulerFunnel { ViewedAt = t0.AddSeconds(10), DatePickedAt = t0.AddSeconds(40) };
        var text = Encoding.UTF8.GetString(VisitorReportCsv.Interactions(new[] { session }));
        var kinds = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')[2]).ToArray();
        Assert.Equal(new[] { "scheduler_view", "click", "scheduler_date_picked" }, kinds);
    }

    internal static VisitorSession Sample()
    {
        var started = new DateTime(2026, 10, 17, 14, 5, 0, DateTimeKind.Utc);
        return new VisitorSession
        {
            Id = Guid.NewGuid(),
            VisitorId = Guid.NewGuid(),
            VisitNumber = 1,
            StartedAt = started,
            LastSeenAt = started.AddMinutes(3),
            Ip = "203.0.113.9",
            Geo = new VisitorGeo { Country = "US", Region = "FL", City = "Delray Beach", Timezone = "America/New_York" },
            Florida = true,
            UserAgent = new VisitorUserAgent { Raw = "ua", Browser = "Chrome", Os = "Windows", DeviceType = "desktop" },
            Pages = new List<VisitorPageView>
            {
                new() { PageViewId = "pv1", Path = "/", EnteredAt = started, ActiveMs = 12_000, MaxScrollPct = 40 },
            },
            Score = 2.5,
        };
    }
}
