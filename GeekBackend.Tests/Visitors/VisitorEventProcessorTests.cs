using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorEventProcessorTests
{
    private const string Ip = "203.0.113.77";

    private static (VisitorEventProcessor Processor, InMemoryVisitorStore Store, CapturingLogger<VisitorEventProcessor> Log, FakeTimeProvider Clock) Make()
    {
        var store = new InMemoryVisitorStore();
        var log = new CapturingLogger<VisitorEventProcessor>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero));
        return (new VisitorEventProcessor(store, new VisitorUserAgentParser(), clock, log), store, log, clock);
    }

    [Fact]
    public async Task A_florida_session_that_crosses_sixty_is_marked_once_and_keeps_the_first_time()
    {
        var (processor, store, _, clock) = Make();
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();

        Assert.True((await processor.ProcessAsync(PageView(visitor, session, "pv1", "/use-cases/marketing", florida: true, snapshot: true), CancellationToken.None)).IsSuccess);
        Assert.True((await processor.ProcessAsync(PageEnd(visitor, session, "pv1", 120_000), CancellationToken.None)).IsSuccess);
        Assert.Null(store.Sessions[session].IntentReachedAt);

        // Two more pages, two minutes on the audit page and the scheduler push it past 60:
        // 10 + 2 + 30 + 10 bonus + 25 = 77.
        Assert.True((await processor.ProcessAsync(PageView(visitor, session, "pv2", "/tools/x", florida: true), CancellationToken.None)).IsSuccess);
        Assert.True((await processor.ProcessAsync(PageView(visitor, session, "pv3", "/architectural-infrastructure-audit", florida: true), CancellationToken.None)).IsSuccess);
        Assert.True((await processor.ProcessAsync(PageEnd(visitor, session, "pv3", 120_000), CancellationToken.None)).IsSuccess);
        Assert.Null(store.Sessions[session].IntentReachedAt);
        Assert.Equal(0, store.IntentMarkAttempts);
        Assert.True((await processor.ProcessAsync(Scheduler(visitor, session, VisitorSchedulerSteps.View), CancellationToken.None)).IsSuccess);

        var marked = store.Sessions[session].IntentReachedAt;
        Assert.Equal(clock.GetUtcNow().UtcDateTime, marked);
        Assert.True(store.Sessions[session].Score >= 60);
        Assert.Equal(1, store.IntentMarkAttempts);

        // More activity on the same session marks nothing more and moves nothing.
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await processor.ProcessAsync(PageEnd(visitor, session, "pv2", 120_000), CancellationToken.None)).IsSuccess);
        Assert.Equal(marked, store.Sessions[session].IntentReachedAt);
        Assert.Equal(1, store.IntentMarkAttempts);
    }

    [Fact]
    public async Task A_session_outside_florida_is_never_marked()
    {
        var (processor, store, _, _) = Make();
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        await processor.ProcessAsync(PageView(visitor, session, "pv1", "/proposals/acme", florida: false), CancellationToken.None);
        await processor.ProcessAsync(PageEnd(visitor, session, "pv1", 120_000), CancellationToken.None);
        await processor.ProcessAsync(PageView(visitor, session, "pv2", "/pricing", florida: false), CancellationToken.None);
        await processor.ProcessAsync(Scheduler(visitor, session, VisitorSchedulerSteps.View), CancellationToken.None);
        Assert.True(store.Sessions[session].Score >= 60);
        Assert.Null(store.Sessions[session].IntentReachedAt);
        Assert.Equal(0, store.IntentMarkAttempts);
    }

    [Fact]
    public async Task A_failed_mark_is_reported_and_the_next_event_tries_again()
    {
        var (processor, store, _, _) = Make();
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        store.FailIntentMarks = true;
        // Two minutes on a proposal (50) and a pricing page (10) with the distinct-page bonus (5) is 65.
        await processor.ProcessAsync(PageView(visitor, session, "pv1", "/proposals/acme", florida: true), CancellationToken.None);
        await processor.ProcessAsync(PageEnd(visitor, session, "pv1", 120_000), CancellationToken.None);
        var failed = await processor.ProcessAsync(PageView(visitor, session, "pv2", "/pricing", florida: true), CancellationToken.None);
        Assert.False(failed.IsSuccess);
        Assert.Equal(VisitorEventProcessor.StoreFailed, failed.ErrorCode);
        Assert.Null(store.Sessions[session].IntentReachedAt);

        store.FailIntentMarks = false;
        Assert.True((await processor.ProcessAsync(PageEnd(visitor, session, "pv2", 120_000), CancellationToken.None)).IsSuccess);
        Assert.NotNull(store.Sessions[session].IntentReachedAt);
    }

    [Fact]
    public async Task A_browser_position_inside_florida_marks_a_session_whose_request_had_no_region()
    {
        var (processor, store, _, _) = Make();
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        var view = PageView(visitor, session, "pv1", "/", florida: false);
        view.Request.Geo = new VisitorGeo();
        await processor.ProcessAsync(view, CancellationToken.None);
        Assert.False(store.Sessions[session].Florida);

        var geo = Event(visitor, session, VisitorEventKinds.Geo);
        geo.Position = new VisitorBrowserPosition { Latitude = 26.4615, Longitude = -80.0728, AccuracyMeters = 35.4 };
        Assert.True((await processor.ProcessAsync(geo, CancellationToken.None)).IsSuccess);
        var stored = store.Sessions[session];
        Assert.True(stored.Florida);
        Assert.Equal(26.4615, stored.BrowserGeo!.Latitude);
        Assert.Equal(35, stored.BrowserGeo.AccuracyMeters);
    }

    [Fact]
    public async Task Identify_names_the_visitor_and_stamps_the_booking_on_the_session()
    {
        var (processor, store, _, _) = Make();
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        await processor.ProcessAsync(PageView(visitor, session, "pv1", "/", florida: true), CancellationToken.None);
        var result = await processor.IdentifyAsync(new VisitorIdentifyRequest
        {
            VisitorId = visitor, SessionId = session, FirstName = "Jane", LastName = "Smith", Email = "jane@example.com", Phone = "5615550100", Source = "booking",
        }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("jane@example.com", store.Profiles[visitor].Identity!.Email);
        Assert.NotNull(store.Sessions[session].Scheduler?.BookedAt);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    public async Task An_unknown_kind_is_refused_as_a_bad_request(string kind)
    {
        var (processor, _, _, _) = Make();
        var e = Event(Guid.NewGuid(), Guid.NewGuid(), kind);
        var result = await processor.ProcessAsync(e, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(VisitorEventProcessor.BadRequest, result.ErrorCode);
    }

    [Fact]
    public async Task A_store_failure_is_logged_without_the_ip_or_the_body()
    {
        var (processor, store, log, _) = Make();
        store.FailWrites = true;
        var e = PageView(Guid.NewGuid(), Guid.NewGuid(), "pv1", "/secret-path", florida: true, snapshot: true);
        var result = await processor.ProcessAsync(e, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(VisitorEventProcessor.StoreFailed, result.ErrorCode);
        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, line =>
        {
            Assert.DoesNotContain(Ip, line);
            Assert.DoesNotContain("/secret-path", line);
            Assert.DoesNotContain("Mozilla", line);
        });
    }

    [Fact]
    public async Task Drops_are_counted_under_the_new_york_day()
    {
        var (processor, store, _, _) = Make();
        var result = await processor.CountDropAsync(new VisitorDropRequest { Reason = "bot", ReceivedAt = new DateTime(2026, 10, 18, 3, 30, 0, DateTimeKind.Utc) }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.FilterDays["2026-10-17"].Counts["bot"]);
        var bad = await processor.CountDropAsync(new VisitorDropRequest { Reason = "made_up" }, CancellationToken.None);
        Assert.Equal(VisitorEventProcessor.BadRequest, bad.ErrorCode);
    }

    internal static VisitorPulseEvent Event(Guid visitor, Guid session, string kind) => new()
    {
        Kind = kind,
        VisitorId = visitor,
        SessionId = session,
        ReceivedAt = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc),
        Request = new VisitorRequestFields
        {
            Ip = Ip,
            Geo = new VisitorGeo { Country = "US", Region = "FL", City = "Delray Beach", Timezone = "America/New_York" },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
            EdgeRegion = "iad1",
        },
    };

    internal static VisitorPulseEvent PageView(Guid visitor, Guid session, string pvid, string path, bool florida, bool snapshot = false)
    {
        var e = Event(visitor, session, VisitorEventKinds.PageView);
        e.PageViewId = pvid;
        e.Path = path;
        e.Title = "A page";
        if (!florida) e.Request.Geo = new VisitorGeo { Country = "US", Region = "GA", City = "Atlanta" };
        if (snapshot)
        {
            e.Snapshot = new VisitorSessionSnapshot
            {
                Arrival = new VisitorArrival { LandingUrl = "https://geekatyourspot.com" + path, Referrer = "https://www.google.com/", Utm = new VisitorUtm { Source = "google", Medium = "cpc" } },
                Environment = new VisitorEnvironment { Screen = new VisitorScreen { Width = 1920, Height = 1080 }, Browser = new VisitorBrowserInfo { Languages = new List<string> { "en-US" } } },
            };
        }
        return e;
    }

    internal static VisitorPulseEvent PageEnd(Guid visitor, Guid session, string pvid, long activeMs)
    {
        var e = Event(visitor, session, VisitorEventKinds.PageEnd);
        e.PageViewId = pvid;
        e.ActiveMs = activeMs;
        e.MaxScrollPct = 80;
        return e;
    }

    internal static VisitorPulseEvent Scheduler(Guid visitor, Guid session, string step)
    {
        var e = Event(visitor, session, VisitorEventKinds.Scheduler);
        e.SchedulerStep = step;
        return e;
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines) Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}
