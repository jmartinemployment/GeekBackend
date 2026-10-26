using GeekAPI.HttpClients;
using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorDailyReportHostedServiceTests
{
    private static VisitorDailyReportHostedService Make(InMemoryVisitorStore store, FakeTimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVisitorRepositoryClient>(store);
        services.AddScoped<VisitorDailyReportComposer>();
        var provider = services.BuildServiceProvider();
        return new VisitorDailyReportHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<VisitorDailyReportHostedService>.Instance);
    }

    /// <summary>06:59 New York on 18 Oct 2026 (EDT, UTC-4).</summary>
    private static DateTimeOffset BeforeSeven => new(2026, 10, 18, 10, 59, 0, TimeSpan.Zero);

    [Fact]
    public async Task Before_seven_it_does_nothing_and_at_seven_it_builds_once_and_purges()
    {
        var store = new InMemoryVisitorStore();
        var clock = new FakeTimeProvider(BeforeSeven);
        var service = Make(store, clock);

        Assert.False(await service.TickAsync(CancellationToken.None));
        Assert.Empty(store.DailyReports);
        Assert.Equal(0, store.PurgeCalls);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await service.TickAsync(CancellationToken.None));
        var built = store.DailyReports["2026-10-17"];
        Assert.Equal(BeforeSeven.AddMinutes(1).UtcDateTime, built.BuiltAt);
        Assert.Equal(0, built.Totals!.Sessions);
        Assert.Equal(1, store.PurgeCalls);
        Assert.NotNull(built.Purge);
        Assert.Null(built.Purge!.Error);
        Assert.Equal(0, built.Purge.Sessions);
        Assert.Equal(0, built.Purge.Visitors);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await service.TickAsync(CancellationToken.None));
        Assert.Equal(1, store.PurgeCalls);
    }

    [Fact]
    public async Task A_failed_compose_releases_the_claim_and_the_next_tick_retries()
    {
        var store = new InMemoryVisitorStore { FailReads = true };
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(5));
        var service = Make(store, clock);

        // The claim read fails too, so the first tick never claims; a store that cannot be read
        // is not a day to claim.
        Assert.False(await service.TickAsync(CancellationToken.None));
        Assert.Empty(store.DailyReports);

        store.FailReads = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await service.TickAsync(CancellationToken.None));
        Assert.NotNull(store.DailyReports["2026-10-17"].BuiltAt);
    }

    [Fact]
    public async Task A_compose_that_fails_after_the_claim_releases_it()
    {
        var store = new InMemoryVisitorStore();
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(5));
        var service = Make(store, clock);

        // Profiles cannot be read, so the compose fails after the claim was won.
        var sample = VisitorReportCsvTests.Sample();
        sample.StartedAt = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
        store.Sessions[sample.Id] = sample;
        var failing = new FailingProfilesStore(store);
        var services = new ServiceCollection();
        services.AddSingleton<IVisitorRepositoryClient>(failing);
        services.AddScoped<VisitorDailyReportComposer>();
        var provider = services.BuildServiceProvider();
        var withFailingProfiles = new VisitorDailyReportHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<VisitorDailyReportHostedService>.Instance);

        Assert.False(await withFailingProfiles.TickAsync(CancellationToken.None));
        Assert.Empty(store.DailyReports);
        Assert.Equal(1, store.ReleaseCalls);
        Assert.Equal(0, store.PurgeCalls);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await service.TickAsync(CancellationToken.None));
        Assert.Equal(1, store.DailyReports["2026-10-17"].Totals!.Sessions);
    }

    [Fact]
    public async Task A_row_that_cannot_be_written_releases_the_claim_and_the_next_tick_builds_again()
    {
        var store = new InMemoryVisitorStore { FailBuiltMarks = true };
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(3));
        var service = Make(store, clock);

        Assert.False(await service.TickAsync(CancellationToken.None));
        Assert.Empty(store.DailyReports);
        Assert.Equal(1, store.ReleaseCalls);
        Assert.Equal(1, store.PurgeCalls);

        store.FailBuiltMarks = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await service.TickAsync(CancellationToken.None));
        Assert.NotNull(store.DailyReports["2026-10-17"].BuiltAt);
        Assert.Equal(2, store.PurgeCalls);
    }

    [Fact]
    public async Task A_failed_purge_is_recorded_on_the_day_with_its_error_and_the_day_still_counts_as_built()
    {
        var store = new InMemoryVisitorStore { FailPurges = true };
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(3));
        var service = Make(store, clock);

        Assert.True(await service.TickAsync(CancellationToken.None));
        var built = store.DailyReports["2026-10-17"];
        Assert.NotNull(built.BuiltAt);
        Assert.NotNull(built.Purge);
        Assert.Equal("GeekRepository answered 503.", built.Purge!.Error);
        Assert.Null(built.Purge.Sessions);
        Assert.Null(built.Purge.Visitors);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, built.Purge.At);
    }

    [Fact]
    public async Task A_claim_with_no_build_behind_it_is_taken_over_after_ten_minutes_and_not_before()
    {
        var store = new InMemoryVisitorStore();
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(30));
        var service = Make(store, clock);

        store.DailyReports["2026-10-17"] = new VisitorDailyReport { Id = "2026-10-17", ClaimedAt = clock.GetUtcNow().UtcDateTime.AddMinutes(-5) };
        Assert.False(await service.TickAsync(CancellationToken.None));
        Assert.Null(store.DailyReports["2026-10-17"].BuiltAt);
        Assert.Equal(0, store.ReleaseCalls);

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await service.TickAsync(CancellationToken.None));
        Assert.Equal(1, store.ReleaseCalls);
        Assert.NotNull(store.DailyReports["2026-10-17"].BuiltAt);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, store.DailyReports["2026-10-17"].ClaimedAt);
    }

    [Fact]
    public async Task Two_services_racing_build_one_report()
    {
        var store = new InMemoryVisitorStore { ComposeDelay = TimeSpan.FromMilliseconds(50) };
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(10));
        var first = Make(store, clock);
        var second = Make(store, clock);

        var results = await Task.WhenAll(first.TickAsync(CancellationToken.None), second.TickAsync(CancellationToken.None));
        Assert.Equal(1, results.Count(r => r));
        Assert.Single(store.DailyReports);
        Assert.Equal(1, store.PurgeCalls);
    }

    [Fact]
    public async Task The_build_records_yesterdays_totals_with_the_days_drops_and_the_intent_sessions()
    {
        var store = new InMemoryVisitorStore();
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(2));
        var service = Make(store, clock);

        var sample = VisitorReportCsvTests.Sample();
        sample.StartedAt = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
        sample.Score = 72;
        sample.IntentReachedAt = sample.StartedAt.AddMinutes(2);
        store.Sessions[sample.Id] = sample;
        store.FilterDays["2026-10-17"] = new VisitorFilterDay { Id = "2026-10-17", Counts = { ["bot"] = 7 } };

        Assert.True(await service.TickAsync(CancellationToken.None));
        var totals = store.DailyReports["2026-10-17"].Totals!;
        Assert.Equal(1, totals.Sessions);
        Assert.Equal(1, totals.FloridaSessions);
        Assert.Equal(1, totals.IntentSessions);
        Assert.Equal(7, totals.Drops["bot"]);
    }

    [Fact]
    public async Task The_purge_after_the_build_removes_what_is_past_the_retention()
    {
        var store = new InMemoryVisitorStore();
        var clock = new FakeTimeProvider(BeforeSeven.AddMinutes(2));
        var service = Make(store, clock);

        var stale = VisitorReportCsvTests.Sample();
        stale.StartedAt = clock.GetUtcNow().UtcDateTime.AddDays(-200);
        stale.LastSeenAt = stale.StartedAt.AddMinutes(1);
        store.Sessions[stale.Id] = stale;
        var fresh = VisitorReportCsvTests.Sample();
        fresh.StartedAt = clock.GetUtcNow().UtcDateTime.AddDays(-20);
        fresh.LastSeenAt = fresh.StartedAt.AddMinutes(1);
        store.Sessions[fresh.Id] = fresh;

        Assert.True(await service.TickAsync(CancellationToken.None));
        Assert.DoesNotContain(stale.Id, store.Sessions.Keys);
        Assert.Contains(fresh.Id, store.Sessions.Keys);
    }

    /// <summary>Everything through the real store except the profile read, which fails.</summary>
    private sealed class FailingProfilesStore(InMemoryVisitorStore inner) : IVisitorRepositoryClient
    {
        public Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand c, CancellationToken ct) => inner.PageViewAsync(c, ct);
        public Task<Result<VisitorSession>> PageEndAsync(Guid id, VisitorPageEndCommand c, CancellationToken ct) => inner.PageEndAsync(id, c, ct);
        public Task<Result<VisitorSession>> ClickAsync(Guid id, VisitorClickCommand c, CancellationToken ct) => inner.ClickAsync(id, c, ct);
        public Task<Result<VisitorSession>> VitalsAsync(Guid id, VisitorVitalsCommand c, CancellationToken ct) => inner.VitalsAsync(id, c, ct);
        public Task<Result<VisitorSession>> SchedulerStepAsync(Guid id, VisitorSchedulerStepCommand c, CancellationToken ct) => inner.SchedulerStepAsync(id, c, ct);
        public Task<Result<VisitorSession>> ContactSubmittedAsync(Guid id, VisitorContactCommand c, CancellationToken ct) => inner.ContactSubmittedAsync(id, c, ct);
        public Task<Result<VisitorSession>> BrowserGeoAsync(Guid id, VisitorBrowserGeoCommand c, CancellationToken ct) => inner.BrowserGeoAsync(id, c, ct);
        public Task<Result<VisitorSession>> ScoreAsync(Guid id, VisitorScoreCommand c, CancellationToken ct) => inner.ScoreAsync(id, c, ct);
        public Task<Result<VisitorSession>> GetSessionAsync(Guid id, CancellationToken ct) => inner.GetSessionAsync(id, ct);
        public Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(DateTime f, DateTime t, CancellationToken ct) => inner.ListSessionsAsync(f, t, ct);
        public Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(Guid v, DateTime s, CancellationToken ct) => inner.ListSessionsForVisitorAsync(v, s, ct);
        public Task<Result<bool>> MarkIntentReachedAsync(Guid id, VisitorIntentReachedCommand c, CancellationToken ct) => inner.MarkIntentReachedAsync(id, c, ct);
        public Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand c, CancellationToken ct) => inner.IdentifyAsync(c, ct);
        public Task<Result<VisitorProfile>> GetProfileAsync(Guid v, CancellationToken ct) => inner.GetProfileAsync(v, ct);
        public Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<VisitorProfile>>.Failure("GeekRepository answered 503."));
        public Task<Result> CountDropAsync(VisitorDropCommand c, CancellationToken ct) => inner.CountDropAsync(c, ct);
        public Task<Result<VisitorFilterDay>> GetFilterDayAsync(string d, CancellationToken ct) => inner.GetFilterDayAsync(d, ct);
        public Task<Result<bool>> ClaimDailyReportAsync(string d, VisitorDailyReportClaimCommand c, CancellationToken ct) => inner.ClaimDailyReportAsync(d, c, ct);
        public Task<Result> MarkDailyReportBuiltAsync(string d, VisitorDailyReportBuiltCommand c, CancellationToken ct) => inner.MarkDailyReportBuiltAsync(d, c, ct);
        public Task<Result> ReleaseDailyReportAsync(string d, VisitorDailyReportReleaseCommand c, CancellationToken ct) => inner.ReleaseDailyReportAsync(d, c, ct);
        public Task<Result<VisitorDailyReport>> GetDailyReportAsync(string d, CancellationToken ct) => inner.GetDailyReportAsync(d, ct);
        public Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand c, CancellationToken ct) => inner.PurgeAsync(c, ct);
    }
}
