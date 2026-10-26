extern alias GeekRepo;

using GeekApplication.Models.Visitors;
using GeekRepo::GeekRepository.Data;
using GeekRepo::GeekRepository.Repositories.Visitors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// The geek_visitors script and the EF mapping against a real Postgres: the script applies, a
/// session with its pages and clicks round-trips, the sequence cursor pages, a parallel claim
/// has one winner, and the purge cascades. Runs only with TEST_DATABASE_URL set; the in-memory
/// suite in GeekBackend.Tests covers the repository's rules on every run.
/// </summary>
public sealed class VisitorSchemaIntegrationTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_schema_applies_and_a_session_round_trips_through_postgres()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();

        var options = new DbContextOptionsBuilder<VisitorsDbContext>()
            .UseNpgsql(IntegrationTestConfig.DatabaseUrl!, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;
        VisitorRepository Repo() => new(new VisitorsDbContext(options), NullLogger<VisitorRepository>.Instance);

        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        var command = new VisitorPageViewCommand
        {
            SessionId = session,
            VisitorId = visitor,
            ReceivedAt = T0,
            Ip = "203.0.113.9",
            Geo = new VisitorGeo { Country = "US", Region = "FL", City = "Delray Beach", Latitude = 26.46, Longitude = -80.07, Timezone = "America/New_York" },
            Florida = true,
            UserAgent = new VisitorUserAgent { Raw = "Mozilla/5.0", Browser = "Chrome", BrowserVersion = "130", Os = "Windows", DeviceType = "desktop" },
            ClientHints = new VisitorClientHints { Mobile = "?0" },
            AcceptLanguage = "en-US,en;q=0.9",
            Page = new VisitorPageView { PageViewId = "pv1", Path = "/use-cases/marketing", Query = "?x=1", Title = "Marketing" },
            Snapshot = new VisitorSessionSnapshot
            {
                Arrival = new VisitorArrival { LandingUrl = "https://geekatyourspot.com/use-cases/marketing", Referrer = "https://www.google.com/", Utm = new VisitorUtm { Source = "google", Medium = "cpc" }, ClickIds = new VisitorClickIds { Gclid = "abc" } },
                Environment = new VisitorEnvironment
                {
                    Screen = new VisitorScreen { Width = 1920, Height = 1080, DevicePixelRatio = 1.5 },
                    Device = new VisitorDevice { Platform = "Win32", HardwareConcurrency = 8, Hover = true },
                    Browser = new VisitorBrowserInfo { Languages = new List<string> { "en-US", "en" }, Timezone = "America/New_York", UtcOffsetMinutes = -240, CookieEnabled = true },
                    HighEntropy = new VisitorHighEntropyHints { PlatformVersion = "15.0.0", FullVersionList = new List<string> { "Chromium;130.0.0.0" } },
                    Network = new VisitorNetwork { EffectiveType = "4g", Downlink = 10, Rtt = 50, SaveData = false },
                },
            },
        };

        var written = await Repo().PageViewAsync(command);
        Assert.True(written.IsSuccess, written.Error);
        await Repo().ClickAsync(session, new VisitorClickCommand { PageViewId = "pv1", ReceivedAt = T0.AddSeconds(5), Click = new VisitorPageClick { Href = "/tools", LinkId = "gys-link-tools" } });
        await Repo().VitalsAsync(session, new VisitorVitalsCommand { PageViewId = "pv1", Name = "LCP", Value = 1200.5 });
        await Repo().SchedulerStepAsync(session, new VisitorSchedulerStepCommand { Step = "view", At = T0.AddSeconds(30) });
        await Repo().ScoreAsync(session, new VisitorScoreCommand { Score = 61 });
        await Repo().IdentifyAsync(new VisitorIdentifyCommand { VisitorId = visitor, SessionId = session, Identity = new VisitorIdentity { FirstName = "Jane", LastName = "Smith", Email = "jane@example.com", Phone = "5615550100", Source = "booking", IdentifiedAt = T0.AddMinutes(1) } });

        var read = await Repo().GetSessionAsync(session);
        Assert.True(read.IsSuccess, read.Error);
        var s = read.Value!;
        Assert.True(s.Sequence > 0);
        Assert.Equal(1, s.VisitNumber);
        Assert.Equal("Delray Beach", s.Geo.City);
        Assert.Equal(26.46, s.Geo.Latitude);
        Assert.Equal("?0", s.ClientHints!.Mobile);
        Assert.Equal(new[] { "en-US", "en" }, s.Environment!.Browser!.Languages);
        Assert.Equal(new[] { "Chromium;130.0.0.0" }, s.Environment.HighEntropy!.FullVersionList);
        Assert.Equal(1.5, s.Environment.Screen!.DevicePixelRatio);
        Assert.True(s.Environment.Device!.Hover);
        Assert.Equal("4g", s.Environment.Network!.EffectiveType);
        Assert.Equal("abc", s.Arrival!.ClickIds!.Gclid);
        Assert.Equal("cpc", s.Arrival.Utm!.Medium);
        var page = Assert.Single(s.Pages);
        Assert.Equal("?x=1", page.Query);
        Assert.Equal(1200.5, page.Vitals!.Lcp);
        var click = Assert.Single(page.Clicks);
        Assert.Equal("gys-link-tools", click.LinkId);
        Assert.Equal(T0.AddSeconds(5), click.At);
        Assert.Equal(DateTimeKind.Utc, click.At.Kind);
        Assert.Equal(T0.AddSeconds(30), s.Scheduler!.ViewedAt);
        Assert.Equal(T0.AddMinutes(1), s.Scheduler.BookedAt);
        Assert.Equal(61, s.Score);
        Assert.Null(s.IntentReachedAt);
        Assert.Null(s.BrowserGeo);
        Assert.Null(s.ContactSubmittedAt);

        var profile = await Repo().GetProfileAsync(visitor);
        Assert.Equal("jane@example.com", profile.Value!.Identity!.Email);

        var marks = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Repo().MarkIntentReachedAsync(session, new VisitorIntentReachedCommand { At = T0.AddMinutes(2).AddMilliseconds(i) })));
        Assert.All(marks, m => Assert.True(m.IsSuccess, m.Error));
        Assert.Equal(1, marks.Count(m => m.Value));

        // The date may already be claimed from an earlier run against the same database, so the
        // assertion is on the once-only rule, not on this run winning.
        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Repo().ClaimDailyReportAsync("2026-10-17", new VisitorDailyReportClaimCommand { ClaimedAt = T0.AddMilliseconds(i) })));
        Assert.All(claims, c => Assert.True(c.IsSuccess, c.Error));
        Assert.True(claims.Count(c => c.Value) <= 1);

        var listed = await Repo().ListSessionsAsync(T0.AddHours(-1), T0.AddHours(1), null, null, 500);
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Contains(listed.Value!, x => x.Id == session);
        var cursor = listed.Value!.Single(x => x.Id == session);
        var after = await Repo().ListSessionsAsync(T0.AddHours(-1), T0.AddHours(1), cursor.StartedAt, cursor.Sequence, 500);
        Assert.True(after.IsSuccess, after.Error);
        Assert.DoesNotContain(after.Value!, x => x.Id == session);

        var purged = await Repo().PurgeAsync(new VisitorPurgeCommand { AsOf = T0.AddDays(181) });
        Assert.True(purged.IsSuccess, purged.Error);
        Assert.Equal(GeekApplication.Results.ResultStatus.NotFound, (await Repo().GetSessionAsync(session)).Status);
        await using var db = new VisitorsDbContext(options);
        Assert.Equal(0, await db.Pages.CountAsync(p => p.SessionId == session));
        Assert.Equal(0, await db.Clicks.CountAsync(c => c.SessionId == session));
    }
}
