using System.Text.RegularExpressions;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Visitors;
using GeekRepository.Repositories.Visitors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Visitors;

/// <summary>
/// The real repository on the in-memory provider: visit numbers, caps, the greater-of and
/// first-of rules, the once-only marks and claims under twenty parallel writers, the range query,
/// retention, and a store that cannot be reached. The schema file is read against the EF model so
/// a column the script does not create cannot be mapped.
/// </summary>
public sealed class VisitorRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One in-memory store per test. Postgres fills row_no as an identity column; the in-memory
    /// provider fills nothing it was not told to, so the store gets a client-side counter for it,
    /// added to the model after the context's own mapping and nowhere near production code.
    /// </summary>
    private static DbContextOptions<VisitorsDbContext> Store() =>
        new DbContextOptionsBuilder<VisitorsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCustomizer, RowNoModelCustomizer>()
            .Options;

    private sealed class RowNoModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<VisitorSessionRow>().Property(x => x.RowNo).HasValueGenerator<RowNoGenerator>();
        }
    }

    private sealed class RowNoGenerator : ValueGenerator<long>
    {
        private static long _last;
        public override bool GeneratesTemporaryValues => false;
        public override long Next(EntityEntry entry) => Interlocked.Increment(ref _last);
    }

    private static VisitorRepository Repo(DbContextOptions<VisitorsDbContext> store) =>
        new(new VisitorsDbContext(store), NullLogger<VisitorRepository>.Instance);

    private static VisitorPageViewCommand PageView(Guid session, Guid visitor, string pvid, string path, DateTime? at = null, string ip = "203.0.113.9") => new()
    {
        SessionId = session,
        VisitorId = visitor,
        ReceivedAt = at ?? T0,
        Ip = ip,
        Geo = new VisitorGeo { Country = "US", Region = "FL", City = "Delray Beach", Timezone = "America/New_York" },
        Florida = true,
        UserAgent = new VisitorUserAgent { Raw = "ua", Browser = "Chrome", DeviceType = "desktop" },
        Page = new VisitorPageView { PageViewId = pvid, Path = path, Title = "t" },
        Snapshot = new VisitorSessionSnapshot
        {
            Arrival = new VisitorArrival { LandingUrl = "https://geekatyourspot.com" + path, Utm = new VisitorUtm { Source = "google" } },
            Environment = new VisitorEnvironment
            {
                Screen = new VisitorScreen { Width = 1920, Height = 1080 },
                Browser = new VisitorBrowserInfo { Languages = new List<string> { "en-US", "es" }, Timezone = "America/New_York" },
            },
        },
    };

    [Fact]
    public async Task A_new_session_increments_the_visitor_and_takes_its_visit_number_and_a_repeat_does_not()
    {
        var store = Store();
        var repo = Repo(store);
        var visitor = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();

        var first = await repo.PageViewAsync(PageView(s1, visitor, "pv1", "/"));
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(1, first.Value!.VisitNumber);
        Assert.True(first.Value.Florida);
        Assert.Single(first.Value.Pages);
        Assert.Equal("https://geekatyourspot.com/", first.Value.Arrival!.LandingUrl);
        Assert.Equal("google", first.Value.Arrival.Utm!.Source);
        Assert.Null(first.Value.Arrival.ClickIds);
        Assert.Equal(new[] { "en-US", "es" }, first.Value.Environment!.Browser!.Languages);
        Assert.Equal(1920, first.Value.Environment.Screen!.Width);
        Assert.Null(first.Value.Environment.Network);
        Assert.Null(first.Value.Scheduler);
        Assert.Null(first.Value.BrowserGeo);
        Assert.Equal(DateTimeKind.Utc, first.Value.StartedAt.Kind);
        Assert.True(first.Value.Sequence > 0);

        var again = await repo.PageViewAsync(PageView(s1, visitor, "pv2", "/tools", T0.AddMinutes(1)));
        Assert.Equal(1, again.Value!.VisitNumber);
        Assert.Equal(2, again.Value.Pages.Count);
        Assert.Equal(new[] { "/", "/tools" }, again.Value.Pages.Select(p => p.Path));
        Assert.Equal(T0.AddMinutes(1), again.Value.LastSeenAt);
        Assert.Equal(1, (await repo.GetProfileAsync(visitor)).Value!.SessionCount);

        var second = await Repo(store).PageViewAsync(PageView(s2, visitor, "pv1", "/", T0.AddHours(2)));
        Assert.Equal(2, second.Value!.VisitNumber);
        Assert.True(second.Value.Sequence > first.Value.Sequence);
        var profile = (await repo.GetProfileAsync(visitor)).Value!;
        Assert.Equal(2, profile.SessionCount);
        Assert.Equal(T0.AddHours(2), profile.LastSeenAt);
        Assert.Equal(T0, profile.FirstSeenAt);
    }

    [Fact]
    public async Task Duplicate_page_view_ids_the_page_cap_the_click_cap_and_the_ip_cap_hold()
    {
        var repo = Repo(Store());
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();

        await repo.PageViewAsync(PageView(session, visitor, "pv1", "/"));
        var dup = await repo.PageViewAsync(PageView(session, visitor, "pv1", "/"));
        Assert.Single(dup.Value!.Pages);

        for (var i = 2; i <= VisitorRepository.MaxPagesPerSession + 5; i++)
            await repo.PageViewAsync(PageView(session, visitor, $"pv{i}", $"/p/{i}"));
        var capped = await repo.GetSessionAsync(session);
        Assert.Equal(VisitorRepository.MaxPagesPerSession, capped.Value!.Pages.Count);
        Assert.Equal("/", capped.Value.Pages[0].Path);

        for (var i = 0; i < VisitorRepository.MaxClicksPerPage + 5; i++)
            await repo.ClickAsync(session, new VisitorClickCommand { PageViewId = "pv1", ReceivedAt = T0.AddSeconds(i), Click = new VisitorPageClick { Href = $"/x/{i}" } });
        var clicks = (await repo.GetSessionAsync(session)).Value!.Pages.Single(p => p.PageViewId == "pv1").Clicks;
        Assert.Equal(VisitorRepository.MaxClicksPerPage, clicks.Count);
        Assert.Equal("/x/0", clicks[0].Href);
        Assert.Equal("/x/49", clicks[^1].Href);

        for (var i = 0; i < VisitorRepository.MaxAdditionalIps + 5; i++)
            await repo.PageViewAsync(PageView(session, visitor, $"ip{i}", "/", ip: $"198.51.100.{i}"));
        await repo.PageViewAsync(PageView(session, visitor, "ipdup", "/", ip: "198.51.100.0"));
        var ips = (await repo.GetSessionAsync(session)).Value!;
        Assert.Equal("203.0.113.9", ips.Ip);
        Assert.Equal(VisitorRepository.MaxAdditionalIps, ips.AdditionalIps.Count);
        Assert.Equal(ips.AdditionalIps.Count, ips.AdditionalIps.Distinct().Count());
    }

    [Fact]
    public async Task Active_time_and_scroll_only_grow_and_a_step_keeps_its_first_time()
    {
        var repo = Repo(Store());
        var session = Guid.NewGuid();
        await repo.PageViewAsync(PageView(session, Guid.NewGuid(), "pv1", "/"));

        await repo.PageEndAsync(session, new VisitorPageEndCommand { PageViewId = "pv1", ActiveMs = 9000, MaxScrollPct = 70, ReceivedAt = T0 });
        var lower = await repo.PageEndAsync(session, new VisitorPageEndCommand { PageViewId = "pv1", ActiveMs = 4000, MaxScrollPct = 20, ReceivedAt = T0 });
        Assert.Equal(9000, lower.Value!.Pages[0].ActiveMs);
        Assert.Equal(70, lower.Value.Pages[0].MaxScrollPct);

        await repo.SchedulerStepAsync(session, new VisitorSchedulerStepCommand { Step = "view", At = T0.AddSeconds(30) });
        var later = await repo.SchedulerStepAsync(session, new VisitorSchedulerStepCommand { Step = "view", At = T0.AddSeconds(90) });
        Assert.Equal(T0.AddSeconds(30), later.Value!.Scheduler!.ViewedAt);
        var earlier = await repo.SchedulerStepAsync(session, new VisitorSchedulerStepCommand { Step = "view", At = T0.AddSeconds(10) });
        Assert.Equal(T0.AddSeconds(10), earlier.Value!.Scheduler!.ViewedAt);
        Assert.Equal(T0.AddSeconds(90), earlier.Value.LastSeenAt);
        Assert.False((await repo.SchedulerStepAsync(session, new VisitorSchedulerStepCommand { Step = "nope", At = T0 })).IsSuccess);

        await repo.VitalsAsync(session, new VisitorVitalsCommand { PageViewId = "pv1", Name = "LCP", Value = 1234.5 });
        var vitals = await repo.VitalsAsync(session, new VisitorVitalsCommand { PageViewId = "pv1", Name = "CLS", Value = 0.02 });
        Assert.Equal(1234.5, vitals.Value!.Pages[0].Vitals!.Lcp);
        Assert.Equal(0.02, vitals.Value.Pages[0].Vitals!.Cls);
        Assert.False((await repo.VitalsAsync(session, new VisitorVitalsCommand { PageViewId = "pv1", Name = "NOPE", Value = 1 })).IsSuccess);

        await repo.ScoreAsync(session, new VisitorScoreCommand { Score = 42.5 });
        var score = await repo.ScoreAsync(session, new VisitorScoreCommand { Score = 10 });
        Assert.Equal(42.5, score.Value!.Score);

        var geo = await repo.BrowserGeoAsync(session, new VisitorBrowserGeoCommand
        {
            Position = new VisitorBrowserGeo { Latitude = 26.4615, Longitude = -80.0728, AccuracyMeters = 35, At = T0.AddMinutes(2) },
            MarkFlorida = true,
        });
        Assert.Equal(26.4615, geo.Value!.BrowserGeo!.Latitude);
        Assert.True(geo.Value.Florida);

        var contact = await repo.ContactSubmittedAsync(session, new VisitorContactCommand { At = T0.AddMinutes(3) });
        Assert.Equal(T0.AddMinutes(3), contact.Value!.ContactSubmittedAt);
        var contactAgain = await repo.ContactSubmittedAsync(session, new VisitorContactCommand { At = T0.AddMinutes(4) });
        Assert.Equal(T0.AddMinutes(3), contactAgain.Value!.ContactSubmittedAt);

        var missing = await repo.PageEndAsync(Guid.NewGuid(), new VisitorPageEndCommand { PageViewId = "pv1", ActiveMs = 1, ReceivedAt = T0 });
        Assert.Equal(ResultStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task Twenty_parallel_intent_marks_have_one_winner_and_the_mark_keeps_its_time()
    {
        var store = Store();
        var session = Guid.NewGuid();
        await Repo(store).PageViewAsync(PageView(session, Guid.NewGuid(), "pv1", "/"));

        var at = T0.AddMinutes(5);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Repo(store).MarkIntentReachedAsync(session, new VisitorIntentReachedCommand { At = at.AddMilliseconds(i) })));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(1, results.Count(r => r.Value));

        var marked = (await Repo(store).GetSessionAsync(session)).Value!.IntentReachedAt;
        Assert.NotNull(marked);
        Assert.InRange(marked.Value, at, at.AddMilliseconds(19));

        var again = await Repo(store).MarkIntentReachedAsync(session, new VisitorIntentReachedCommand { At = at.AddHours(1) });
        Assert.False(again.Value);
        Assert.Equal(marked, (await Repo(store).GetSessionAsync(session)).Value!.IntentReachedAt);

        var missing = await Repo(store).MarkIntentReachedAsync(Guid.NewGuid(), new VisitorIntentReachedCommand { At = at });
        Assert.Equal(ResultStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task Twenty_parallel_report_claims_have_one_winner_and_a_release_frees_the_date()
    {
        var store = Store();
        var date = "2026-10-17";
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Repo(store).ClaimDailyReportAsync(date, new VisitorDailyReportClaimCommand { ClaimedAt = T0.AddMilliseconds(i) })));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(1, results.Count(r => r.Value));

        var repo = Repo(store);
        var held = (await repo.GetDailyReportAsync(date)).Value!;
        Assert.Null(held.BuiltAt);
        Assert.Null(held.Totals);
        await repo.ReleaseDailyReportAsync(date, new VisitorDailyReportReleaseCommand { ClaimedAt = held.ClaimedAt.AddSeconds(1) });
        Assert.True((await repo.GetDailyReportAsync(date)).IsSuccess);
        await repo.ReleaseDailyReportAsync(date, new VisitorDailyReportReleaseCommand { ClaimedAt = held.ClaimedAt });
        Assert.Equal(ResultStatus.NotFound, (await repo.GetDailyReportAsync(date)).Status);

        Assert.True((await repo.ClaimDailyReportAsync(date, new VisitorDailyReportClaimCommand { ClaimedAt = T0.AddHours(1) })).Value);
        await repo.CountDropAsync(new VisitorDropCommand { Day = date, Reason = "bot" });
        await repo.MarkDailyReportBuiltAsync(date, new VisitorDailyReportBuiltCommand
        {
            BuiltAt = T0.AddHours(1),
            Totals = new VisitorReportTotals { Sessions = 3, IntentSessions = 1, Drops = { ["bot"] = 99 } },
            Purge = new VisitorPurgeOutcome { At = T0.AddHours(1).AddSeconds(5), Sessions = 12, Visitors = 4 },
        });
        await repo.ReleaseDailyReportAsync(date, new VisitorDailyReportReleaseCommand { ClaimedAt = T0.AddHours(1) });
        var built = (await repo.GetDailyReportAsync(date)).Value!;
        Assert.Equal(T0.AddHours(1), built.BuiltAt);
        Assert.Equal(3, built.Totals!.Sessions);
        Assert.Equal(1, built.Totals.IntentSessions);
        // The drops come from the day's own counts, not from the totals that were handed in.
        Assert.Equal(1, built.Totals.Drops["bot"]);
        Assert.Equal(T0.AddHours(1).AddSeconds(5), built.Purge!.At);
        Assert.Equal(12, built.Purge.Sessions);
        Assert.Equal(4, built.Purge.Visitors);
        Assert.Null(built.Purge.Error);

        // A purge that failed is written with its error and no counts.
        await repo.MarkDailyReportBuiltAsync(date, new VisitorDailyReportBuiltCommand
        {
            BuiltAt = T0.AddHours(1),
            Totals = built.Totals,
            Purge = new VisitorPurgeOutcome { At = T0.AddHours(2), Error = " Visitor store write failed. " },
        });
        var failedPurge = (await repo.GetDailyReportAsync(date)).Value!.Purge!;
        Assert.Equal("Visitor store write failed.", failedPurge.Error);
        Assert.Null(failedPurge.Sessions);
        Assert.Null(failedPurge.Visitors);
        Assert.False((await repo.ClaimDailyReportAsync(date, new VisitorDailyReportClaimCommand { ClaimedAt = T0.AddHours(2) })).Value);
        Assert.Equal("not_found", (await repo.MarkDailyReportBuiltAsync("2026-10-18", new VisitorDailyReportBuiltCommand { BuiltAt = T0 })).ErrorCode);
    }

    [Fact]
    public async Task Identify_sets_the_identity_and_the_latest_submission_wins()
    {
        var repo = Repo(Store());
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        await repo.PageViewAsync(PageView(session, visitor, "pv1", "/"));

        await repo.IdentifyAsync(new VisitorIdentifyCommand { VisitorId = visitor, SessionId = session, Identity = new VisitorIdentity { FirstName = "J", LastName = "S", Email = "a@example.com", Phone = "1", Source = "contact", IdentifiedAt = T0 } });
        var second = await repo.IdentifyAsync(new VisitorIdentifyCommand { VisitorId = visitor, SessionId = session, Identity = new VisitorIdentity { FirstName = "Jane", LastName = "Smith", Email = "b@example.com", Phone = "2", Source = "booking", IdentifiedAt = T0.AddMinutes(1) } });
        Assert.Equal("b@example.com", second.Value!.Identity!.Email);
        Assert.Equal("Jane", second.Value.Identity.FirstName);
        Assert.Equal(1, second.Value.SessionCount);
        var s = (await repo.GetSessionAsync(session)).Value!;
        Assert.Equal(T0, s.ContactSubmittedAt);
        Assert.Equal(T0.AddMinutes(1), s.Scheduler!.BookedAt);

        var unseen = Guid.NewGuid();
        var fresh = await repo.IdentifyAsync(new VisitorIdentifyCommand { VisitorId = unseen, Identity = new VisitorIdentity { Email = "c@example.com", Source = "contact", IdentifiedAt = T0 } });
        Assert.Equal(0, fresh.Value!.SessionCount);
        Assert.Equal("c@example.com", (await repo.GetProfileAsync(unseen)).Value!.Identity!.Email);
        Assert.False((await repo.IdentifyAsync(new VisitorIdentifyCommand { VisitorId = unseen, Identity = new VisitorIdentity { Email = " " } })).IsSuccess);
    }

    [Fact]
    public async Task Drops_count_per_day_and_reason_under_parallel_writers_and_refuse_an_unknown_reason()
    {
        var store = Store();
        var day = "2026-10-17";
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Repo(store).CountDropAsync(new VisitorDropCommand { Day = day, Reason = "bot" })));
        var repo = Repo(store);
        await repo.CountDropAsync(new VisitorDropCommand { Day = day, Reason = "gpc" });
        Assert.False((await repo.CountDropAsync(new VisitorDropCommand { Day = day, Reason = "whatever" })).IsSuccess);
        Assert.False((await repo.CountDropAsync(new VisitorDropCommand { Day = "17/10/2026", Reason = "bot" })).IsSuccess);
        var counts = (await repo.GetFilterDayAsync(day)).Value!.Counts;
        Assert.Equal(20, counts["bot"]);
        Assert.Equal(1, counts["gpc"]);
        Assert.Equal(2, counts.Count);
        Assert.Equal(ResultStatus.NotFound, (await repo.GetFilterDayAsync("2026-10-18")).Status);
    }

    [Fact]
    public async Task The_range_query_pages_in_started_order_on_the_sequence_cursor()
    {
        var repo = Repo(Store());
        var visitor = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await repo.PageViewAsync(PageView(id, visitor, "pv1", "/", T0.AddMinutes(i)));
        }
        // Two sessions sharing a start instant are told apart by the sequence.
        var twinA = Guid.NewGuid();
        var twinB = Guid.NewGuid();
        await repo.PageViewAsync(PageView(twinA, visitor, "pv1", "/", T0.AddMinutes(3)));
        await repo.PageViewAsync(PageView(twinB, visitor, "pv1", "/", T0.AddMinutes(3)));

        var all = new List<VisitorSession>();
        DateTime? afterStartedAt = null;
        long? afterSequence = null;
        while (true)
        {
            var page = await repo.ListSessionsAsync(T0, T0.AddMinutes(5), afterStartedAt, afterSequence, 3);
            Assert.True(page.IsSuccess, page.Error);
            all.AddRange(page.Value!);
            if (page.Value!.Count < 3) break;
            afterStartedAt = page.Value[^1].StartedAt;
            afterSequence = page.Value[^1].Sequence;
        }
        Assert.Equal(7, all.Count);
        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
        Assert.Equal(new[] { ids[0], ids[1], ids[2], ids[3], twinA, twinB, ids[4] }, all.Select(s => s.Id));
        Assert.True(all.Zip(all.Skip(1)).All(pair => pair.First.StartedAt < pair.Second.StartedAt || pair.First.Sequence < pair.Second.Sequence));

        var forVisitor = await repo.ListSessionsForVisitorAsync(visitor, T0.AddMinutes(4), 100);
        Assert.Equal(3, forVisitor.Value!.Count);
        Assert.Equal(T0.AddMinutes(6), forVisitor.Value[0].StartedAt);
    }

    [Fact]
    public async Task The_purge_removes_sessions_and_visitors_past_the_retention_with_their_pages_and_keeps_the_rest()
    {
        var store = Store();
        var repo = Repo(store);
        var old = Guid.NewGuid();
        var oldVisitor = Guid.NewGuid();
        var recent = Guid.NewGuid();
        var recentVisitor = Guid.NewGuid();
        var asOf = new DateTime(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await repo.PageViewAsync(PageView(old, oldVisitor, "pv1", "/", asOf.AddDays(-181)));
        await repo.ClickAsync(old, new VisitorClickCommand { PageViewId = "pv1", ReceivedAt = asOf.AddDays(-181), Click = new VisitorPageClick { Href = "/x" } });
        await repo.PageViewAsync(PageView(recent, recentVisitor, "pv1", "/", asOf.AddDays(-179)));

        var purged = await repo.PurgeAsync(new VisitorPurgeCommand { AsOf = asOf });
        Assert.True(purged.IsSuccess, purged.Error);
        Assert.Equal(1, purged.Value!.Sessions);
        Assert.Equal(1, purged.Value.Visitors);
        Assert.Equal(ResultStatus.NotFound, (await repo.GetSessionAsync(old)).Status);
        Assert.Equal(ResultStatus.NotFound, (await repo.GetProfileAsync(oldVisitor)).Status);
        Assert.True((await repo.GetSessionAsync(recent)).IsSuccess);
        Assert.True((await repo.GetProfileAsync(recentVisitor)).IsSuccess);

        await using var db = new VisitorsDbContext(store);
        Assert.Equal(1, await db.Pages.CountAsync());
        Assert.Equal(0, await db.Clicks.CountAsync());
        Assert.Equal(recent, (await db.Pages.SingleAsync()).SessionId);

        var nothing = await repo.PurgeAsync(new VisitorPurgeCommand { AsOf = asOf });
        Assert.Equal(0, nothing.Value!.Sessions);
        Assert.False((await repo.PurgeAsync(new VisitorPurgeCommand())).IsSuccess);
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_answers_failures_and_never_throws()
    {
        var options = new DbContextOptionsBuilder<VisitorsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Username=nobody;Password=nothing;Database=none;Timeout=1;Command Timeout=1")
            .Options;
        var repo = new VisitorRepository(new VisitorsDbContext(options), NullLogger<VisitorRepository>.Instance);

        var write = await repo.PageViewAsync(PageView(Guid.NewGuid(), Guid.NewGuid(), "pv1", "/"));
        Assert.Equal(ResultStatus.Failure, write.Status);
        Assert.Equal("Visitor store write failed.", write.Error);
        var read = await repo.GetSessionAsync(Guid.NewGuid());
        Assert.Equal(ResultStatus.Failure, read.Status);
        var claim = await repo.ClaimDailyReportAsync("2026-10-17", new VisitorDailyReportClaimCommand { ClaimedAt = T0 });
        Assert.Equal(ResultStatus.Failure, claim.Status);
        var purge = await repo.PurgeAsync(new VisitorPurgeCommand { AsOf = T0 });
        Assert.Equal(ResultStatus.Failure, purge.Status);
    }

    [Fact]
    public void Every_mapped_column_is_created_by_the_schema_script_and_nothing_else_is()
    {
        var sql = File.ReadAllText(SchemaScriptPath());
        var tables = Regex.Matches(sql, @"CREATE TABLE IF NOT EXISTS geek_visitors\.(\w+) \((.*?)\n\);", RegexOptions.Singleline)
            .ToDictionary(
                m => m.Groups[1].Value,
                m => m.Groups[2].Value.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)
                        && !line.StartsWith("PRIMARY KEY", StringComparison.Ordinal)
                        && !line.StartsWith("FOREIGN KEY", StringComparison.Ordinal)
                        && !line.StartsWith("REFERENCES", StringComparison.Ordinal))
                    .Select(line => line.Split(' ', 2)[0])
                    .ToHashSet(StringComparer.Ordinal));

        using var db = new VisitorsDbContext(new DbContextOptionsBuilder<VisitorsDbContext>().UseInMemoryDatabase("columns").Options);
        var mapped = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            Assert.Equal(VisitorsDbContext.Schema, entity.GetSchema());
            mapped[table] = entity.GetProperties().Select(p => p.GetColumnName()).ToHashSet(StringComparer.Ordinal);
        }

        Assert.Equal(tables.Keys.OrderBy(k => k, StringComparer.Ordinal), mapped.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (table, columns) in tables)
        {
            Assert.True(columns.SetEquals(mapped[table]),
                $"{table}: script has [{string.Join(", ", columns.Except(mapped[table]))}] the model lacks; model has [{string.Join(", ", mapped[table].Except(columns))}] the script lacks.");
        }

        Assert.Equal("geo_postal_code", VisitorsDbContext.ToSnakeCase("GeoPostalCode"));
        Assert.Equal("ua_raw", VisitorsDbContext.ToSnakeCase("UaRaw"));
        Assert.Equal("li_fat_id", VisitorsDbContext.ToSnakeCase("LiFatId"));
        Assert.Equal("row_no", VisitorsDbContext.ToSnakeCase("RowNo"));
        Assert.Equal("id", VisitorsDbContext.ToSnakeCase("Id"));
    }

    private static string SchemaScriptPath()
    {
        var inOutput = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql", "0042_geek_visitors_schema.sql");
        if (File.Exists(inOutput)) return inOutput;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Migrations", "Sql", "0042_geek_visitors_schema.sql"));
    }
}
