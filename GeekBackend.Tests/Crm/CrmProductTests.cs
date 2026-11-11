using System.Net;
using System.Reflection;
using System.Text;
using GeekAPI.HttpClients;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Phase 2: the rules for a product, a line item and a holding (CrmProductRules); the store's operations in
/// memory (the catalog, line-item sums, winning a deal into holdings, tenant isolation); and the routes
/// GeekAPI calls on GeekRepository. The SQL itself (the unique keys, the row lock, the rollback of a failed
/// win) is proved by the integration tests against Postgres.
/// </summary>
public class CrmProductTests
{
    private static readonly Guid TenantA = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid TenantB = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2aff");

    // ----- the rules -----

    [Theory]
    [InlineData("melio-ai-invoice-approval")]
    [InlineData("geek-receptionist")]
    [InlineData("a1")]
    public void A_product_code_is_lower_case_letters_digits_and_hyphens(string code) =>
        Assert.Null(CrmProductRules.ValidateProduct("Name", code, ProductKinds.GeekSaas, ProductCategories.Sales, null, BillingPeriods.Monthly));

    [Theory]
    [InlineData("Has Space")]
    [InlineData("UPPER")]
    [InlineData("-lead")]
    [InlineData("trail-")]
    [InlineData("")]
    public void A_product_code_that_breaks_the_rule_is_refused(string code) =>
        Assert.NotNull(CrmProductRules.ValidateProduct("Name", code, ProductKinds.GeekSaas, ProductCategories.Sales, null, BillingPeriods.Monthly));

    [Fact]
    public void A_product_needs_a_known_kind_category_and_billing_and_a_rooted_use_case_path()
    {
        Assert.NotNull(CrmProductRules.ValidateProduct("Name", "x", "gadget", ProductCategories.Sales, null, BillingPeriods.Monthly));
        Assert.NotNull(CrmProductRules.ValidateProduct("Name", "x", ProductKinds.Service, "legal", null, BillingPeriods.Monthly));
        Assert.NotNull(CrmProductRules.ValidateProduct("Name", "x", ProductKinds.Service, ProductCategories.Sales, null, "weekly"));
        Assert.NotNull(CrmProductRules.ValidateProduct(" ", "x", ProductKinds.Service, ProductCategories.Sales, null, BillingPeriods.Monthly));
        Assert.NotNull(CrmProductRules.ValidateProduct("Name", "x", ProductKinds.Service, ProductCategories.Sales, "use-cases/x", BillingPeriods.Monthly));
        Assert.Null(CrmProductRules.ValidateProduct("Name", "x", ProductKinds.Service, ProductCategories.Sales, "/use-cases/accounting/x", BillingPeriods.Monthly));
    }

    [Theory]
    [InlineData(1, 100, 0, 100.00)]
    [InlineData(3, 49.99, 10, 134.97)]
    [InlineData(1, 0.05, 50, 0.03)]
    [InlineData(2, 12.345, 0, 24.69)]
    public void A_line_total_is_quantity_times_price_less_discount_rounded_half_away_from_zero(double quantity, double price, double discount, double total) =>
        Assert.Equal((decimal)total, CrmProductRules.LineTotal((decimal)quantity, (decimal)price, (decimal)discount));

    [Fact]
    public void A_line_item_needs_a_positive_quantity_a_price_and_a_discount_from_0_to_100()
    {
        Assert.Null(CrmProductRules.ValidateLineItem(1, 0, 0));
        Assert.NotNull(CrmProductRules.ValidateLineItem(0, 10, 0));
        Assert.NotNull(CrmProductRules.ValidateLineItem(1, -1, 0));
        Assert.NotNull(CrmProductRules.ValidateLineItem(1, 10, 101));
        Assert.NotNull(CrmProductRules.ValidateLineItem(1.234m, 10, 0));
        Assert.NotNull(CrmProductRules.ValidateLineItem(1, 10.123m, 0));
    }

    private static HoldingDetails Holding(string? arrangement = null, decimal? referralFee = null, bool? recurringFee = null, decimal? cost = null) =>
        new(HoldingStatuses.Active, null, null, 1, 50, 0, 50, null, arrangement, referralFee, recurringFee, null, cost, null);

    [Fact]
    public void An_arrangement_belongs_to_a_partner_subscription_and_its_fields_to_its_own_arrangement()
    {
        var partner = ProductKinds.PartnerSubscription;
        Assert.Null(CrmProductRules.ValidateHolding(Holding(), ProductKinds.GeekSaas));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding(PartnerArrangements.Resold), ProductKinds.GeekSaas));
        Assert.Null(CrmProductRules.ValidateHolding(Holding(PartnerArrangements.Referral, 100, true), partner));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding(PartnerArrangements.CustomerDirect, 100), partner));
        Assert.Null(CrmProductRules.ValidateHolding(Holding(PartnerArrangements.Resold, cost: 20), partner));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding(PartnerArrangements.Referral, cost: 20), partner));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding("barter"), partner));
    }

    [Fact]
    public void A_holding_needs_a_known_status_and_cannot_end_before_it_starts()
    {
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding() with { Status = "dormant" }, ProductKinds.Service));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding() with { StartOn = new DateOnly(2026, 11, 2), EndOn = new DateOnly(2026, 11, 1) }, ProductKinds.Service));
        Assert.NotNull(CrmProductRules.ValidateHolding(Holding() with { RecurringAmountMonthly = -1 }, ProductKinds.Service));
    }

    // ----- the store, in memory -----

    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("crm-products-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }
        public Guid Organization { get; } = Guid.NewGuid();
        public Guid Person { get; } = Guid.NewGuid();
        public Guid Partner { get; } = Guid.NewGuid();
        public Guid OtherTenantOrganization { get; } = Guid.NewGuid();
        public Guid Pipeline { get; } = Guid.NewGuid();
        public Guid NewStage { get; } = Guid.NewGuid();
        public Guid WonStage { get; } = Guid.NewGuid();

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.AddRange(new CrmTenantRow { Id = TenantA, Name = "A", Slug = "a" }, new CrmTenantRow { Id = TenantB, Name = "B", Slug = "b" });
            Db.Participants.AddRange(
                new CrmParticipantRow { Id = Organization, TenantId = TenantA, Kind = "organization", DisplayName = "Boca Dental", Source = "import" },
                new CrmParticipantRow { Id = Person, TenantId = TenantA, Kind = "person", DisplayName = "Maria Lopez", Source = "import" },
                new CrmParticipantRow { Id = Partner, TenantId = TenantA, Kind = "organization", DisplayName = "Melio", Source = "import" },
                new CrmParticipantRow { Id = OtherTenantOrganization, TenantId = TenantB, Kind = "organization", DisplayName = "Other", Source = "import" });
            Db.Pipelines.Add(new CrmPipelineRow { Id = Pipeline, TenantId = TenantA, Name = "Sales", IsDefault = true });
            Db.Stages.AddRange(
                new CrmStageRow { Id = NewStage, TenantId = TenantA, PipelineId = Pipeline, Name = "New", Position = 1, Outcome = "open" },
                new CrmStageRow { Id = WonStage, TenantId = TenantA, PipelineId = Pipeline, Name = "Won", Position = 2, Outcome = "won" });
            Db.SaveChanges();
        }

        public async Task<CrmProduct> ProductAsync(string code, string kind = ProductKinds.GeekSaas, string billing = BillingPeriods.Monthly, Guid? tenant = null)
        {
            var made = await Repository.CreateProductAsync(new(tenant ?? TenantA, code + " name", code, kind, ProductCategories.Accounting, null, null, billing));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public async Task<Guid> DealAsync(bool withOrganization = true)
        {
            var made = await Repository.CreateDealAsync(new(TenantA, "Boca Dental, AI Receptionist", "ai_receptionist",
                withOrganization ? Organization : null, null, null, null, null, null));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!.Id;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_product_is_created_listed_updated_and_retired_never_deleted()
    {
        using var store = new Store();
        var product = await store.ProductAsync("geek-receptionist");
        Assert.True(product.IsActive);

        var retired = await store.Repository.UpdateProductAsync(product.Id, new(TenantA, "Geek-Receptionist", ProductCategories.BusinessOperations, "/use-cases/x", null, BillingPeriods.Monthly, false));
        Assert.True(retired.IsSuccess, retired.Error);
        Assert.Equal(("geek-receptionist", ProductKinds.GeekSaas, false), (retired.Value!.Code, retired.Value.Kind, retired.Value.IsActive));

        Assert.Empty((await store.Repository.ListProductsAsync(TenantA, null, false)).Value!);
        Assert.Single((await store.Repository.ListProductsAsync(TenantA, null, true)).Value!);
        Assert.Empty((await store.Repository.ListProductsAsync(TenantA, ProductKinds.Service, true)).Value!);
    }

    [Fact]
    public async Task A_second_product_with_the_same_code_is_a_conflict_and_another_tenant_may_reuse_it()
    {
        using var store = new Store();
        await store.ProductAsync("quickbooks");

        var again = await store.Repository.CreateProductAsync(new(TenantA, "Other", "quickbooks", ProductKinds.Service, ProductCategories.Sales, null, null, BillingPeriods.OneTime));
        Assert.Equal(CrmFailureCodes.Conflict, again.ErrorCode);
        Assert.True((await store.ProductAsync("quickbooks", tenant: TenantB)).IsActive);
    }

    [Fact]
    public async Task A_partner_must_be_an_organization_of_the_same_tenant()
    {
        using var store = new Store();
        CreateProductCommand With(Guid partner) => new(TenantA, "Melio AP", "melio-ap", ProductKinds.PartnerSubscription, ProductCategories.Accounting, null, partner, BillingPeriods.Monthly);

        Assert.True((await store.Repository.CreateProductAsync(With(store.Partner))).IsSuccess);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateProductAsync(With(store.Person))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateProductAsync(With(store.OtherTenantOrganization))).ErrorCode);
    }

    [Fact]
    public async Task A_deals_totals_become_the_sums_of_its_line_items()
    {
        using var store = new Store();
        var dealId = await store.DealAsync();
        var saas = await store.ProductAsync("geek-receptionist");
        var setup = await store.ProductAsync("receptionist-setup", ProductKinds.Service, BillingPeriods.OneTime);

        var first = await store.Repository.AddLineItemAsync(dealId, new(TenantA, saas.Id, 2, 49.99m, null, 10));
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(Assert.Single(first.Value!.Items).IsRecurring);
        Assert.Equal(89.98m, first.Value.RecurringValueMonthly);

        var both = await store.Repository.AddLineItemAsync(dealId, new(TenantA, setup.Id, 1, 297m, null, 0));
        Assert.Equal((297m, 89.98m), (both.Value!.EstimatedValue, both.Value.RecurringValueMonthly));
        Assert.Equal(2, both.Value.Items.Count);
        Assert.Equal(89.98m, both.Value.Items[0].LineTotal);

        var changed = await store.Repository.UpdateLineItemAsync(dealId, both.Value.Items[1].Id, new(TenantA, 1, 350m, false, 0));
        Assert.Equal(350m, changed.Value!.EstimatedValue);

        var removed = await store.Repository.DeleteLineItemAsync(TenantA, dealId, both.Value.Items[1].Id);
        Assert.Equal(1, removed.Value!.Items.Count);
    }

    [Fact]
    public async Task A_line_item_needs_a_known_active_product_of_the_same_tenant()
    {
        using var store = new Store();
        var dealId = await store.DealAsync();
        var other = await store.ProductAsync("elsewhere", tenant: TenantB);
        var retired = await store.ProductAsync("retired-one");
        await store.Repository.UpdateProductAsync(retired.Id, new(TenantA, "x", ProductCategories.Sales, null, null, BillingPeriods.Monthly, false));

        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.AddLineItemAsync(dealId, new(TenantA, other.Id, 1, 10, null, 0))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.AddLineItemAsync(dealId, new(TenantA, retired.Id, 1, 10, null, 0))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.AddLineItemAsync(dealId, new(TenantA, retired.Id, 0, 10, null, 0))).ErrorCode);
    }

    private static async Task<(Store Store, Guid DealId, CrmDealLineItems Items)> DealWithTwoItemsAsync()
    {
        var store = new Store();
        var dealId = await store.DealAsync();
        var saas = await store.ProductAsync("geek-receptionist");
        var setup = await store.ProductAsync("receptionist-setup", ProductKinds.Service, BillingPeriods.OneTime);
        await store.Repository.AddLineItemAsync(dealId, new(TenantA, saas.Id, 1, 49m, null, 0));
        var items = await store.Repository.AddLineItemAsync(dealId, new(TenantA, setup.Id, 1, 297m, null, 0));
        store.Db.DealParticipants.Add(new CrmDealParticipantRow { Id = Guid.NewGuid(), TenantId = TenantA, DealId = dealId, ParticipantId = store.Person, Role = DealParticipantRoles.PrimaryContact });
        await store.Db.SaveChangesAsync();
        return (store, dealId, items.Value!);
    }

    [Fact]
    public async Task Winning_a_deal_makes_one_active_holding_per_line_item_and_a_repeat_makes_none()
    {
        var (store, dealId, items) = await DealWithTwoItemsAsync();
        using var _ = store;

        var won = await store.Repository.WinDealAsync(TenantA, dealId, "test");
        Assert.True(won.IsSuccess, won.Error);
        Assert.Equal(("Won", StageOutcomes.Won, 2), (won.Value!.Deal.StageName, won.Value.Deal.StageOutcome, won.Value.HoldingsCreated));
        Assert.Equal(2, won.Value.Holdings.Count);
        Assert.All(won.Value.Holdings, h => Assert.Equal((HoldingStatuses.Active, store.Organization, dealId), (h.Status, h.ParticipantId, h.DealId)));
        var recurring = Assert.Single(won.Value.Holdings, h => h.RecurringAmountMonthly is not null);
        var once = Assert.Single(won.Value.Holdings, h => h.OneTimeAmount is not null);
        Assert.Equal((49m, 297m), (recurring.RecurringAmountMonthly, once.OneTimeAmount));
        Assert.Equal(items.Items.Select(i => i.Id).Order(), won.Value.Holdings.Select(h => h.DealLineItemId!.Value).Order());

        var again = await store.Repository.WinDealAsync(TenantA, dealId, "test");
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(0, again.Value!.HoldingsCreated);
        Assert.Equal(2, again.Value.Holdings.Count);
        Assert.Equal(2, await store.Db.CustomerHoldings.CountAsync());
    }

    [Fact]
    public async Task A_won_deals_line_items_are_frozen()
    {
        var (store, dealId, items) = await DealWithTwoItemsAsync();
        using var _ = store;
        await store.Repository.WinDealAsync(TenantA, dealId, "test");
        var product = (await store.Repository.ListProductsAsync(TenantA, null, false)).Value![0];

        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.AddLineItemAsync(dealId, new(TenantA, product.Id, 1, 1, null, 0))).ErrorCode);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.UpdateLineItemAsync(dealId, items.Items[0].Id, new(TenantA, 1, 1, true, 0))).ErrorCode);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.DeleteLineItemAsync(TenantA, dealId, items.Items[0].Id)).ErrorCode);
    }

    [Fact]
    public async Task A_deal_without_line_items_or_without_anyone_to_hold_them_cannot_be_won()
    {
        using var store = new Store();
        var empty = await store.DealAsync();
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.WinDealAsync(TenantA, empty, "test")).ErrorCode);

        var nobody = await store.DealAsync(withOrganization: false);
        var product = await store.ProductAsync("geek-receptionist");
        await store.Repository.AddLineItemAsync(nobody, new(TenantA, product.Id, 1, 49m, null, 0));
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.WinDealAsync(TenantA, nobody, "test")).ErrorCode);
        Assert.Empty(store.Db.CustomerHoldings);
    }

    [Fact]
    public async Task Another_tenant_can_neither_see_nor_win_nor_change_a_deal_or_its_holdings()
    {
        var (store, dealId, items) = await DealWithTwoItemsAsync();
        using var _ = store;
        var won = await store.Repository.WinDealAsync(TenantA, dealId, "test");

        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.WinDealAsync(TenantB, dealId, "x")).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.ListLineItemsAsync(TenantB, dealId)).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.ListHoldingsAsync(TenantB, store.Organization)).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateHoldingAsync(won.Value!.Holdings[0].Id, new(TenantB, Holding())) ).ErrorCode);
        Assert.Empty((await store.Repository.ListProductsAsync(TenantB, null, true)).Value!);
        Assert.NotEmpty(items.Items);
    }

    [Fact]
    public async Task A_customer_may_hold_the_same_product_twice_and_a_partner_subscription_records_its_arrangement()
    {
        using var store = new Store();
        var melio = await store.ProductAsync("melio", ProductKinds.PartnerSubscription);
        var saas = await store.ProductAsync("geek-receptionist");

        var first = await store.Repository.CreateHoldingAsync(new(TenantA, store.Organization, saas.Id, Holding()));
        var second = await store.Repository.CreateHoldingAsync(new(TenantA, store.Organization, saas.Id, Holding()));
        Assert.True(first.IsSuccess && second.IsSuccess, first.Error ?? second.Error);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);

        var referral = await store.Repository.CreateHoldingAsync(new(TenantA, store.Organization, melio.Id, Holding(PartnerArrangements.Referral, 150, false)));
        Assert.True(referral.IsSuccess, referral.Error);
        var resold = await store.Repository.UpdateHoldingAsync(referral.Value!.Id, new(TenantA, Holding(PartnerArrangements.Resold, cost: 20)));
        Assert.True(resold.IsSuccess, resold.Error);
        Assert.Equal((PartnerArrangements.Resold, 20m, (decimal?)null), (resold.Value!.Arrangement, resold.Value.PartnerCostMonthly, resold.Value.ReferralFee));

        var refused = await store.Repository.CreateHoldingAsync(new(TenantA, store.Organization, saas.Id, Holding(PartnerArrangements.Resold)));
        Assert.Equal(CrmFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Equal(3, (await store.Repository.ListHoldingsAsync(TenantA, store.Organization)).Value!.Count);
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
        var root = controller.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>()!.Template;
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
    public async Task Every_phase_2_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var recorder = new Recorder();
        var client = new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance);
        var id = Guid.NewGuid();

        await client.ListProductsAsync(TenantA, ProductKinds.Service, true);
        await client.CreateProductAsync(new(TenantA, "n", "c", ProductKinds.Service, ProductCategories.Sales, null, null, BillingPeriods.OneTime));
        await client.UpdateProductAsync(id, new(TenantA, "n", ProductCategories.Sales, null, null, BillingPeriods.OneTime, true));
        await client.ListLineItemsAsync(TenantA, id);
        await client.AddLineItemAsync(id, new(TenantA, id, 1, 1, null, 0));
        await client.UpdateLineItemAsync(id, id, new(TenantA, 1, 1, true, 0));
        await client.DeleteLineItemAsync(TenantA, id, id);
        await client.WinDealAsync(TenantA, id, "api");
        await client.ListHoldingsAsync(TenantA, id);
        await client.CreateHoldingAsync(new(TenantA, id, id, Holding()));
        await client.UpdateHoldingAsync(id, new(TenantA, Holding()));

        Assert.Equal(11, recorder.Calls.Count);
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
        foreach (var expected in new[]
                 {
                     "GET products", "POST products", "PUT products/{productId}",
                     "GET participants/{participantId}/holdings", "POST participants/{participantId}/holdings", "PUT participants/{participantId}/holdings/{holdingId}",
                     "GET deals/{dealId}/line-items", "POST deals/{dealId}/line-items", "PUT deals/{dealId}/line-items/{lineItemId}", "DELETE deals/{dealId}/line-items/{lineItemId}",
                     "POST deals/{dealId}/win",
                 })
            Assert.Contains(expected, routes);
    }
}
