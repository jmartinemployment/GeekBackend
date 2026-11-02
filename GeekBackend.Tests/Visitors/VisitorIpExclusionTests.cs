using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorIpExclusionTests
{
    private static readonly DateOnly Day = new(2026, 11, 1);

    [Fact]
    public void An_absent_or_blank_value_excludes_nothing()
    {
        Assert.True(VisitorIpExclusion.TryParse(null, out var none, out var error));
        Assert.Empty(none);
        Assert.Null(error);

        Assert.True(VisitorIpExclusion.TryParse(" , ", out var blank, out _));
        Assert.Empty(blank);
    }

    [Fact]
    public void A_list_is_trimmed_and_each_address_written_one_way()
    {
        Assert.True(VisitorIpExclusion.TryParse(" 99.92.181.172 ,::ffff:203.0.113.9, 2001:DB8::1", out var ips, out _));
        Assert.Equal(new[] { "2001:db8::1", "203.0.113.9", "99.92.181.172" }, ips.OrderBy(ip => ip, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("12")]
    [InlineData("99.92.181")]
    [InlineData("99.92.181.172, home")]
    public void An_entry_that_is_not_an_ip_refuses_the_request(string raw)
    {
        Assert.False(VisitorIpExclusion.TryParse(raw, out var ips, out var error));
        Assert.Empty(ips);
        Assert.Contains("is not an IP address", error);
    }

    [Fact]
    public async Task Sessions_from_an_excluded_ip_are_left_out_and_the_report_says_so()
    {
        var store = new InMemoryVisitorStore();
        var kept = Session("203.0.113.1");
        var byIp = Session("99.92.181.172");
        var byAdditionalIp = Session("198.51.100.7", "99.92.181.172");
        foreach (var s in new[] { kept, byIp, byAdditionalIp }) store.Sessions[s.Id] = s;
        VisitorIpExclusion.TryParse("99.92.181.172", out var excluded, out _);

        var result = await new VisitorDailyReportComposer(store).ComposeAsync(Day, excluded, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var report = result.Value!;
        Assert.Equal(1, report.Totals.Sessions);
        Assert.Contains("LEFT OUT BY REQUEST", report.Body);
        Assert.Contains("Sessions from 99.92.181.172 are not counted below: 2 sessions this day.", report.Body);
        var csv = System.Text.Encoding.UTF8.GetString(report.SessionsCsv);
        Assert.Contains(kept.Id.ToString(), csv);
        Assert.DoesNotContain(byIp.Id.ToString(), csv);
        Assert.DoesNotContain(byAdditionalIp.Id.ToString(), csv);
    }

    [Fact]
    public async Task A_day_whose_every_session_was_left_out_is_not_reported_as_nothing_arriving()
    {
        var store = new InMemoryVisitorStore();
        var only = Session("99.92.181.172");
        store.Sessions[only.Id] = only;
        VisitorIpExclusion.TryParse("99.92.181.172", out var excluded, out _);

        var result = await new VisitorDailyReportComposer(store).ComposeAsync(Day, excluded, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Totals.Sessions);
        Assert.DoesNotContain("NOTHING ARRIVED", result.Value.Body);
        Assert.Contains("1 session this day.", result.Value.Body);
    }

    [Fact]
    public async Task Without_an_exclusion_the_report_is_unchanged()
    {
        var store = new InMemoryVisitorStore();
        var s = Session("99.92.181.172");
        store.Sessions[s.Id] = s;

        var result = await new VisitorDailyReportComposer(store).ComposeAsync(Day, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Totals.Sessions);
        Assert.DoesNotContain("LEFT OUT BY REQUEST", result.Value.Body);
    }

    private static VisitorSession Session(string ip, params string[] additionalIps)
    {
        var (start, _) = VisitorClock.DayWindowUtc(Day);
        var startedAt = start.AddHours(10);
        return new VisitorSession
        {
            Id = Guid.NewGuid(),
            VisitorId = Guid.NewGuid(),
            VisitNumber = 1,
            StartedAt = startedAt,
            LastSeenAt = startedAt.AddSeconds(30),
            Ip = ip,
            AdditionalIps = additionalIps.ToList(),
            Geo = new VisitorGeo { Country = "US", Region = "FL", City = "Boca Raton", Timezone = "America/New_York" },
            Florida = true,
            UserAgent = new VisitorUserAgent { Raw = "ua", Browser = "Chrome", DeviceType = "desktop", Os = "Windows" },
            Pages = { new VisitorPageView { PageViewId = "pv0", Path = "/", EnteredAt = startedAt, ActiveMs = 30_000 } },
        };
    }
}
