using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Phase 3: the capacity rules, projects against 40 + 20 hours a week, the capacity read, urgent messages, and the
/// receptionist's one-call intake with its estimate. The store runs in memory; the advisory lock and the concurrent
/// schedules are proved by the integration tests against Postgres.
/// </summary>
public class CrmReceptionistTests
{
    private static readonly Guid Tenant = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly DateOnly Monday = new(2026, 11, 9);

    // ----- the rules -----

    [Theory]
    [InlineData("2026-11-09", "2026-11-09")]
    [InlineData("2026-11-12", "2026-11-09")]
    [InlineData("2026-11-15", "2026-11-09")]
    [InlineData("2026-11-16", "2026-11-16")]
    public void A_week_starts_on_monday(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), CrmCapacityRules.WeekStart(DateOnly.Parse(date)));

    [Fact]
    public void A_project_ends_on_its_target_or_when_its_hours_are_done_at_its_pace()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), CrmCapacityRules.LastDay(Monday, new DateOnly(2026, 11, 20), 100, 10));
        Assert.Equal(Monday.AddDays(3 * 7 - 1), CrmCapacityRules.LastDay(Monday, null, 50, 20));
        Assert.Equal(Monday.AddDays(6), CrmCapacityRules.LastDay(Monday, null, 0, 0));
    }

    [Fact]
    public void A_project_needs_a_known_status_sane_hours_and_a_start_when_it_holds_hours()
    {
        ProjectDetails P(string status = ProjectStatuses.Scheduled, DateOnly? start = null, decimal hours = 10, decimal pace = 10, DateOnly? end = null) => new(status, start ?? Monday, end, hours, pace, null, null);
        Assert.Null(CrmCapacityRules.ValidateProject(P()));
        Assert.NotNull(CrmCapacityRules.ValidateProject(P("paused")));
        Assert.NotNull(CrmCapacityRules.ValidateProject(P(pace: 61)));
        Assert.NotNull(CrmCapacityRules.ValidateProject(P(hours: -1)));
        Assert.NotNull(CrmCapacityRules.ValidateProject(P(end: Monday.AddDays(-1))));
        Assert.NotNull(CrmCapacityRules.ValidateProject(new(ProjectStatuses.Active, null, null, 10, 10, null, null)));
        Assert.Null(CrmCapacityRules.ValidateProject(new(ProjectStatuses.Done, null, null, 10, 10, null, null)));
    }

    private static ReceptionistIntakeCommand Intake(string sid = "CA1", string phone = "+15615550123", IReadOnlyList<IntakeProduct>? products = null, string priority = ActivityPriorities.Normal, IntakeMeeting? meeting = null) =>
        new(Tenant, sid, phone, "Maria", "Lopez", "maria@example.com", "ai_receptionist", JsonDocument.Parse("{\"q1\":\"yes\"}").RootElement.Clone(),
            products ?? [], "Wants a receptionist.", "Transcript", "https://rec.example/1", 120, new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc), priority, meeting);

    [Fact]
    public void An_intake_needs_a_call_a_phone_a_priority_and_sensible_products_and_meeting()
    {
        Assert.Null(CrmCapacityRules.ValidateIntake(Intake()));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(sid: " ")));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(phone: "5615550123")));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(priority: "asap")));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(products: [new("x", "huge")])));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(products: [new("x", null, 0)])));
        Assert.NotNull(CrmCapacityRules.ValidateIntake(Intake(meeting: new(Monday.ToDateTime(new TimeOnly(10, 0)), Monday.ToDateTime(new TimeOnly(9, 0)), null))));
    }

    // ----- the store, in memory -----

    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("crm-receptionist-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }
        public Guid Customer { get; } = Guid.NewGuid();
        public Guid Partner { get; } = Guid.NewGuid();

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.Add(new CrmTenantRow { Id = Tenant, Name = "Geek", Slug = "geek" });
            Db.Participants.AddRange(
                new CrmParticipantRow { Id = Customer, TenantId = Tenant, Kind = "organization", DisplayName = "Boca Dental", Source = "import" },
                new CrmParticipantRow { Id = Partner, TenantId = Tenant, Kind = "organization", DisplayName = "Melio", Source = "import" });
            var pipeline = Guid.NewGuid();
            Db.Pipelines.Add(new CrmPipelineRow { Id = pipeline, TenantId = Tenant, Name = "Sales", IsDefault = true });
            Db.Stages.Add(new CrmStageRow { Id = Guid.NewGuid(), TenantId = Tenant, PipelineId = pipeline, Name = "New", Position = 1, Outcome = "open" });
            Db.SaveChanges();
        }

        public async Task<CrmProject> ProjectAsync(DateOnly start, decimal pace, decimal hours = 0, string status = ProjectStatuses.Scheduled, DateOnly? end = null)
        {
            var made = await Repository.CreateProjectAsync(new(Tenant, Customer, new(status, start, end, hours == 0 ? pace : hours, pace, null, null)));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public async Task<CrmProduct> ProductAsync(string code, string kind, string billing, Guid? partner = null)
        {
            var made = await Repository.CreateProductAsync(new(Tenant, code, code, kind, ProductCategories.Accounting, null, partner, billing));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public Task PriceAsync(Guid product, decimal? unit, decimal? monthly = null, string? size = null, decimal? hours = null) =>
            Repository.SetPriceAsync(product, new(Tenant, size, hours, unit, monthly, new DateOnly(2026, 1, 1)));

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Projects_share_a_week_up_to_sixty_hours_and_the_next_one_that_would_pass_it_is_refused()
    {
        using var store = new Store();
        await store.ProjectAsync(Monday, 35, end: Monday.AddDays(13));
        var over = await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.Scheduled, Monday.AddDays(7), null, 30, 30, null, null)));
        Assert.Equal(CrmFailureCodes.Conflict, over.ErrorCode);
        Assert.Contains("2026-11-16", over.Error, StringComparison.Ordinal);

        Assert.True((await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.Scheduled, Monday.AddDays(7), null, 25, 25, null, null)))).IsSuccess);
        Assert.True((await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.Scheduled, Monday.AddDays(14), null, 60, 60, null, null)))).IsSuccess);
        Assert.Equal(3, (await store.Repository.ListProjectsAsync(Tenant, null, null)).Value!.Count);
    }

    [Fact]
    public async Task A_project_that_is_not_scheduled_or_active_holds_no_hours()
    {
        using var store = new Store();
        var first = await store.ProjectAsync(Monday, 60, end: Monday.AddDays(6));
        Assert.True((await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.OnHold, Monday, null, 40, 40, null, null)))).IsSuccess);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.Active, Monday, null, 1, 1, null, null)))).ErrorCode);

        var done = await store.Repository.UpdateProjectAsync(first.Id, new(Tenant, new(ProjectStatuses.Done, Monday, Monday.AddDays(6), 60, 60, null, null)));
        Assert.True(done.IsSuccess, done.Error);
        Assert.True((await store.Repository.CreateProjectAsync(new(Tenant, store.Customer, new(ProjectStatuses.Active, Monday, null, 40, 40, null, null)))).IsSuccess);
    }

    [Fact]
    public async Task A_decrease_never_fails_and_an_increase_that_passes_sixty_is_refused()
    {
        using var store = new Store();
        var a = await store.ProjectAsync(Monday, 40, end: Monday.AddDays(6));
        var b = await store.ProjectAsync(Monday, 20, end: Monday.AddDays(6));
        // A week already past its limit (put there directly, as an old change could have) still lets hours come down.
        store.Db.Projects.Add(new CrmProjectRow { Id = Guid.NewGuid(), TenantId = Tenant, ParticipantId = store.Customer, Status = ProjectStatuses.Scheduled, StartOn = Monday, TargetEndOn = Monday.AddDays(6), EstimatedHours = 10, HoursPerWeek = 10 });
        await store.Db.SaveChangesAsync();

        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.UpdateProjectAsync(b.Id, new(Tenant, new(ProjectStatuses.Scheduled, Monday, Monday.AddDays(6), 21, 21, null, null)))).ErrorCode);
        var lower = await store.Repository.UpdateProjectAsync(a.Id, new(Tenant, new(ProjectStatuses.Scheduled, Monday, Monday.AddDays(6), 30, 30, null, null)));
        Assert.True(lower.IsSuccess, lower.Error);
        Assert.Equal(30m, lower.Value!.HoursPerWeek);
    }

    [Fact]
    public async Task A_project_belongs_to_a_participant_of_the_same_tenant_and_a_missing_one_is_not_found()
    {
        using var store = new Store();
        var other = await store.Repository.CreateProjectAsync(new(Tenant, Guid.NewGuid(), new(ProjectStatuses.Scheduled, Monday, null, 10, 10, null, null)));
        Assert.Equal(CrmFailureCodes.NotFound, other.ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateProjectAsync(Guid.NewGuid(), new(Tenant, new(ProjectStatuses.Done, null, null, 1, 1, null, null)))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateProjectAsync(Guid.NewGuid(), new(Guid.NewGuid(), new(ProjectStatuses.Done, null, null, 1, 1, null, null)))).ErrorCode);
    }

    [Fact]
    public async Task Capacity_is_read_from_the_project_rows_week_by_week_with_the_uplift_each_week_carries()
    {
        using var store = new Store();
        await store.Repository.ReplaceUpliftTiersAsync(new(Tenant, [new(75m, 10m), new(100m, 25m)]));
        await store.ProjectAsync(Monday, 30, end: Monday.AddDays(6));
        await store.ProjectAsync(Monday.AddDays(7), 50, end: Monday.AddDays(13));
        await store.ProjectAsync(Monday.AddDays(7), 4, end: Monday.AddDays(13));

        var read = await store.Repository.GetCapacityAsync(Tenant, 4, Monday.AddDays(2));
        Assert.True(read.IsSuccess, read.Error);
        var weeks = read.Value!.Weeks;
        Assert.Equal([Monday, Monday.AddDays(7), Monday.AddDays(14), Monday.AddDays(21)], weeks.Select(w => w.WeekStart));
        Assert.Equal([30m, 54m, 0m, 0m], weeks.Select(w => w.BookedHours));
        Assert.Equal([75m, 135m, 0m, 0m], weeks.Select(w => w.BookedPct));
        Assert.Equal([10m, 25m, 0m, 0m], weeks.Select(w => w.UpliftPct));
        Assert.All(weeks, w => Assert.Equal((40m, 20m), (w.BaseHours, w.SurgeHours)));
        Assert.Equal(Monday.AddDays(2), read.Value.EarliestStartOn);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.GetCapacityAsync(Tenant, 0, null)).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.GetCapacityAsync(Tenant, 53, null)).ErrorCode);
    }

    [Fact]
    public async Task The_earliest_start_is_the_first_week_with_base_capacity_free()
    {
        using var store = new Store();
        await store.ProjectAsync(Monday, 40, end: Monday.AddDays(13));
        var read = await store.Repository.GetCapacityAsync(Tenant, 4, Monday);
        Assert.Equal(Monday.AddDays(14), read.Value!.EarliestStartOn);
    }

    // ----- intake -----

    private static async Task<Store> PricedStoreAsync()
    {
        var store = new Store();
        var setup = await store.ProductAsync("melio-workflow", ProductKinds.Implementation, BillingPeriods.OneTime, store.Partner);
        var payments = await store.ProductAsync("melio-payments", ProductKinds.Implementation, BillingPeriods.OneTime, store.Partner);
        var saas = await store.ProductAsync("geek-receptionist", ProductKinds.GeekSaas, BillingPeriods.Monthly);
        await store.PriceAsync(setup.Id, 1000m, size: EstimateSizes.Medium, hours: 10);
        await store.PriceAsync(payments.Id, 500m, size: EstimateSizes.Small, hours: 5);
        await store.PriceAsync(saas.Id, null, 49m);
        await store.Repository.SetPricingRulesAsync(new(Tenant, 150m, 60m));
        return store;
    }

    [Fact]
    public async Task One_call_writes_the_caller_the_deal_its_estimate_the_call_and_the_meeting()
    {
        using var store = await PricedStoreAsync();
        var meeting = new IntakeMeeting(new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc), "evt-1");

        var made = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-workflow", EstimateSizes.Medium), new("geek-receptionist", null)], meeting: meeting));

        Assert.True(made.IsSuccess, made.Error);
        var r = made.Value!;
        Assert.True(r.Created);
        Assert.Empty(r.Problems);
        Assert.NotNull(r.MeetingActivityId);
        Assert.Equal(2, r.LineItems.Count);
        Assert.Equal((1000m, 49m), (r.EstimatedValue, r.RecurringValueMonthly));
        Assert.All(r.LineItems, i => Assert.True(i.IsEstimate));
        Assert.Equal((10m, 150m), (r.LineItems.Single(i => i.Size == EstimateSizes.Medium).Hours, r.LineItems.Single(i => i.Size == EstimateSizes.Medium).HourlyRate));

        Assert.Equal(1, await store.Db.Deals.CountAsync());
        var deal = await store.Db.Deals.SingleAsync();
        Assert.NotNull(deal.EstimateGivenAt);
        Assert.Contains("\"q1\"", deal.Qualification, StringComparison.Ordinal);
        Assert.Equal(2, await store.Db.ContactPoints.CountAsync(c => c.ParticipantId == r.ParticipantId));
        var activities = await store.Db.Activities.Where(a => a.DealId == deal.Id).ToListAsync();
        Assert.Equal([ActivityChannels.Call, ActivityChannels.Meeting], activities.Select(a => a.Channel).Order());
        var booked = activities.Single(a => a.Channel == ActivityChannels.Meeting);
        Assert.Equal(("evt-1", ActivityOutcomes.Booked, meeting.StartsAt), (booked.CalendarEventId, booked.Outcome, booked.StartsAt!.Value));
        Assert.Equal("Transcript", activities.Single(a => a.Channel == ActivityChannels.Call).Transcript);
        Assert.Contains("\"basePrice\"", (await store.Db.DealLineItems.FirstAsync()).EstimateInputs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_call_sid_twice_writes_one_of_each_row_and_answers_the_first_result()
    {
        using var store = await PricedStoreAsync();
        var first = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-workflow", EstimateSizes.Medium)]));
        var again = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-workflow", EstimateSizes.Medium)]));

        Assert.True(first.Value!.Created);
        Assert.False(again.Value!.Created);
        Assert.Equal((first.Value.ParticipantId, first.Value.DealId, first.Value.CallActivityId), (again.Value.ParticipantId, again.Value.DealId, again.Value.CallActivityId));
        Assert.Equal(1, await store.Db.Deals.CountAsync());
        Assert.Equal(1, await store.Db.DealLineItems.CountAsync());
        Assert.Equal(1, await store.Db.Activities.CountAsync(a => a.Channel == ActivityChannels.Call));
        Assert.Equal(1, await store.Db.Persons.CountAsync());

        var next = await store.Repository.ReceptionistIntakeAsync(Intake(sid: "CA2"));
        Assert.True(next.Value!.Created);
        Assert.Equal(first.Value.ParticipantId, next.Value.ParticipantId);
        Assert.Equal(2, await store.Db.Deals.CountAsync());
        Assert.Equal(1, await store.Db.Persons.CountAsync());
    }

    [Fact]
    public async Task A_second_use_case_with_the_same_partner_is_priced_at_the_related_percentage()
    {
        using var store = await PricedStoreAsync();
        var made = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-payments", EstimateSizes.Small), new("melio-workflow", EstimateSizes.Medium)]));

        var lines = made.Value!.LineItems;
        Assert.Equal(0m, lines.Single(i => i.ProductName == "melio-workflow").DiscountPct);
        var related = lines.Single(i => i.ProductName == "melio-payments");
        Assert.Equal((40m, 300m), (related.DiscountPct, related.LineTotal));
        Assert.Equal(1300m, made.Value.EstimatedValue);
    }

    [Fact]
    public async Task Demand_raises_the_price_of_work_priced_by_hours_and_the_estimate_says_what_it_was_based_on()
    {
        using var store = await PricedStoreAsync();
        await store.Repository.ReplaceUpliftTiersAsync(new(Tenant, [new(75m, 10m)]));
        var weekStart = CrmCapacityRules.WeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
        await store.ProjectAsync(weekStart, 36, end: weekStart.AddDays(6));
        await store.ProjectAsync(weekStart.AddDays(7), 36, end: weekStart.AddDays(13));
        await store.ProjectAsync(weekStart.AddDays(14), 36, end: weekStart.AddDays(20));
        await store.ProjectAsync(weekStart.AddDays(21), 36, end: weekStart.AddDays(27));
        await store.ProjectAsync(weekStart.AddDays(28), 36, end: weekStart.AddDays(34));
        await store.ProjectAsync(weekStart.AddDays(35), 36, end: weekStart.AddDays(41));
        await store.ProjectAsync(weekStart.AddDays(42), 36, end: weekStart.AddDays(48));
        await store.ProjectAsync(weekStart.AddDays(49), 36, end: weekStart.AddDays(55));

        var made = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-workflow", EstimateSizes.Medium), new("geek-receptionist", null)]));

        var work = made.Value!.LineItems.Single(i => i.Size == EstimateSizes.Medium);
        Assert.Equal((10m, 1100m), (work.DemandUpliftPct, work.UnitPrice));
        Assert.Null(made.Value.LineItems.Single(i => i.ProductName == "geek-receptionist").DemandUpliftPct);
        Assert.Equal(49m, made.Value.RecurringValueMonthly);
    }

    [Fact]
    public async Task A_product_that_is_unknown_or_unpriced_is_left_out_and_named_never_guessed()
    {
        using var store = await PricedStoreAsync();
        await store.ProductAsync("unpriced", ProductKinds.Service, BillingPeriods.OneTime);
        var made = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("melio-workflow", EstimateSizes.Medium), new("nope", null), new("unpriced", null), new("melio-workflow", EstimateSizes.Large)]));

        Assert.True(made.IsSuccess, made.Error);
        Assert.Single(made.Value!.LineItems);
        Assert.Equal(3, made.Value.Problems.Count);
        Assert.Contains(made.Value.Problems, p => p.Contains("'nope'", StringComparison.Ordinal));
        Assert.Contains(made.Value.Problems, p => p.Contains("'unpriced'", StringComparison.Ordinal));
        Assert.Contains(made.Value.Problems, p => p.Contains("(large)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_pricing_rules_a_related_use_case_is_priced_in_full_and_the_problem_says_so()
    {
        using var store = new Store();
        var a = await store.ProductAsync("a", ProductKinds.Implementation, BillingPeriods.OneTime, store.Partner);
        var b = await store.ProductAsync("b", ProductKinds.Implementation, BillingPeriods.OneTime, store.Partner);
        await store.PriceAsync(a.Id, 100m);
        await store.PriceAsync(b.Id, 50m);
        var made = await store.Repository.ReceptionistIntakeAsync(Intake(products: [new("a", null), new("b", null)]));
        Assert.Equal(150m, made.Value!.EstimatedValue);
        Assert.Contains(made.Value.Problems, p => p.Contains("no pricing rules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_intake_with_a_bad_request_or_an_unknown_tenant_writes_nothing()
    {
        using var store = await PricedStoreAsync();
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.ReceptionistIntakeAsync(Intake(phone: "nope"))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.ReceptionistIntakeAsync(Intake() with { TenantId = Guid.NewGuid() })).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.ReceptionistIntakeAsync(Intake() with { Qualification = JsonDocument.Parse("[1]").RootElement.Clone() })).ErrorCode);
        Assert.Equal(0, await store.Db.Deals.CountAsync());
        Assert.Equal(0, await store.Db.Activities.CountAsync());
    }

    [Fact]
    public async Task Urgent_messages_are_filterable_and_an_activity_priority_is_checked()
    {
        using var store = await PricedStoreAsync();
        await store.Repository.ReceptionistIntakeAsync(Intake(sid: "CA-urgent", priority: ActivityPriorities.Urgent));
        await store.Repository.ReceptionistIntakeAsync(Intake(sid: "CA-normal", phone: "+15615550999"));
        var message = await store.Repository.RecordActivityAsync(new(Tenant, store.Customer, null, ActivityChannels.Voicemail, ActivityDirections.Inbound, DateTime.UtcNow, null, "Our payments stopped", null, null, null, null, "CA-vm", "receptionist", ActivityPriorities.Urgent));
        Assert.True(message.IsSuccess, message.Error);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.RecordActivityAsync(new(Tenant, store.Customer, null, ActivityChannels.Voicemail, null, DateTime.UtcNow, null, null, null, null, null, null, "CA-x", "receptionist", "asap"))).ErrorCode);

        var urgent = (await store.Repository.ListActivitiesAsync(Tenant, ActivityPriorities.Urgent, null, 50)).Value!;
        Assert.Equal(2, urgent.Count);
        Assert.All(urgent, a => Assert.Equal(ActivityPriorities.Urgent, a.Priority));
        Assert.Single((await store.Repository.ListActivitiesAsync(Tenant, ActivityPriorities.Urgent, ActivityChannels.Voicemail, 50)).Value!);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.ListActivitiesAsync(Tenant, "asap", null, 50)).ErrorCode);
        Assert.Empty((await store.Repository.ListActivitiesAsync(Guid.NewGuid(), ActivityPriorities.Urgent, null, 50)).Value!);
    }

    // ----- the routes -----

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
        }
    }

    private static IEnumerable<(string Verb, string Template)> Routes(Type controller)
    {
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                yield return (attribute.HttpMethods.Single(), System.Text.RegularExpressions.Regex.Replace($"{root}/{attribute.Template}", @"\{(\w+):[^}]+\}", "{$1}"));
    }

    private static bool Matches(string template, string path)
    {
        var t = template.Split('/');
        var p = path.Split('/');
        return t.Length == p.Length && t.Zip(p).All(pair => pair.First.StartsWith('{') || pair.First == pair.Second);
    }

    [Fact]
    public async Task Every_phase_3_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var recorder = new Recorder();
        var client = new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance);
        var id = Guid.NewGuid();
        var details = new ProjectDetails(ProjectStatuses.Scheduled, Monday, null, 10, 10, null, null);

        await client.ListProjectsAsync(Tenant, ProjectStatuses.Active, id);
        await client.CreateProjectAsync(new(Tenant, id, details));
        await client.UpdateProjectAsync(id, new(Tenant, details));
        await client.GetCapacityAsync(Tenant, 8, Monday);
        await client.ListActivitiesAsync(Tenant, ActivityPriorities.Urgent, null, 25);
        await client.ReceptionistIntakeAsync(Intake());

        Assert.Equal(6, recorder.Calls.Count);
        var routes = Routes(typeof(GeekRepository.Controllers.Crm.CrmController)).ToList();
        foreach (var call in recorder.Calls)
        {
            var path = call.PathAndQuery[..(call.PathAndQuery.IndexOf('?', StringComparison.Ordinal) is var q and >= 0 ? q : call.PathAndQuery.Length)].TrimStart('/');
            Assert.True(routes.Any(r => r.Verb == call.Method.Method && Matches(r.Template, path)), $"GeekRepository declares no {call.Method} route for {path}.");
        }
    }

    [Fact]
    public void GeekApi_exposes_the_paths_the_plan_names()
    {
        var routes = Routes(typeof(GeekAPI.Controllers.Crm.CrmController)).Select(r => $"{r.Verb} {r.Template["api/crm/".Length..]}").ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "POST receptionist/intake", "GET projects", "POST projects", "PUT projects/{projectId}", "GET capacity", "GET activities" })
            Assert.Contains(expected, routes);
    }
}
