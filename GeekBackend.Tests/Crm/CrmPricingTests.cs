using System.Net;
using System.Reflection;
using System.Text;
using GeekAPI.Controllers.Platform;
using GeekAPI.HttpClients;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Phase 2, the rest: prices with history, the tenant's pricing rules and uplift tiers, tenant subscriptions
/// (platform-scoped) and the platform-admin gate in front of them. The store runs in memory; the SQL (the unique
/// key on a booked level, row-level security on the platform table, the row lock on a product) is proved by the
/// integration tests against Postgres.
/// </summary>
public class CrmPricingTests
{
    private static readonly Guid Seller = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid Subscriber = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2aff");
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Jul = new(2026, 7, 1);

    // ----- the rules -----

    [Fact]
    public void A_price_needs_a_known_size_and_amounts_of_zero_or_more_with_two_decimals()
    {
        Assert.Null(CrmProductRules.ValidatePrice(null, null, null, null));
        Assert.Null(CrmProductRules.ValidatePrice(EstimateSizes.Large, 12.5m, 1000m, 49m));
        Assert.NotNull(CrmProductRules.ValidatePrice("huge", null, 1m, null));
        Assert.NotNull(CrmProductRules.ValidatePrice(null, -1m, 1m, null));
        Assert.NotNull(CrmProductRules.ValidatePrice(null, null, -1m, null));
        Assert.NotNull(CrmProductRules.ValidatePrice(null, null, 1.234m, null));
    }

    [Fact]
    public void Pricing_rules_and_tiers_stay_between_0_and_100_and_a_booked_level_appears_once()
    {
        Assert.Null(CrmProductRules.ValidatePricingRules(150m, 60m));
        Assert.Null(CrmProductRules.ValidatePricingRules(null, 0m));
        Assert.NotNull(CrmProductRules.ValidatePricingRules(-1m, 60m));
        Assert.NotNull(CrmProductRules.ValidatePricingRules(150m, 101m));
        Assert.Null(CrmProductRules.ValidateUpliftTiers([new(75m, 10m), new(90m, 25m)]));
        Assert.Null(CrmProductRules.ValidateUpliftTiers([]));
        Assert.NotNull(CrmProductRules.ValidateUpliftTiers(null));
        Assert.NotNull(CrmProductRules.ValidateUpliftTiers([new(75m, 10m), new(75m, 25m)]));
        Assert.NotNull(CrmProductRules.ValidateUpliftTiers([new(101m, 10m)]));
    }

    [Fact]
    public void A_subscription_needs_a_code_a_known_status_and_cannot_end_before_it_starts()
    {
        Assert.Null(CrmProductRules.ValidateSubscription("geek-receptionist", HoldingStatuses.Active, Jan, null, "Starter"));
        Assert.NotNull(CrmProductRules.ValidateSubscription("Geek Receptionist", HoldingStatuses.Active, Jan, null, null));
        Assert.NotNull(CrmProductRules.ValidateSubscription("geek-receptionist", "dormant", Jan, null, null));
        Assert.NotNull(CrmProductRules.ValidateSubscription("geek-receptionist", HoldingStatuses.Active, Jul, Jan, null));
    }

    // ----- the store, in memory -----

    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("crm-pricing-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }
        public Guid Organization { get; } = Guid.NewGuid();

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.AddRange(new CrmTenantRow { Id = Seller, Name = "Geek", Slug = "geek" }, new CrmTenantRow { Id = Subscriber, Name = "Boca Dental", Slug = "boca" });
            Db.Participants.Add(new CrmParticipantRow { Id = Organization, TenantId = Seller, Kind = "organization", DisplayName = "Boca Dental", Source = "import" });
            var pipeline = Guid.NewGuid();
            Db.Pipelines.Add(new CrmPipelineRow { Id = pipeline, TenantId = Seller, Name = "Sales", IsDefault = true });
            Db.Stages.AddRange(
                new CrmStageRow { Id = Guid.NewGuid(), TenantId = Seller, PipelineId = pipeline, Name = "New", Position = 1, Outcome = "open" },
                new CrmStageRow { Id = Guid.NewGuid(), TenantId = Seller, PipelineId = pipeline, Name = "Won", Position = 2, Outcome = "won" });
            Db.SaveChanges();
        }

        public async Task<CrmProduct> ProductAsync(string code, string kind = ProductKinds.GeekSaas, string billing = BillingPeriods.Monthly)
        {
            var made = await Repository.CreateProductAsync(new(Seller, code + " name", code, kind, ProductCategories.Accounting, null, null, billing));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public Task<GeekApplication.Results.Result<CrmProductPrice>> PriceAsync(Guid product, DateOnly from, decimal? unit, decimal? monthly = null, string? size = null) =>
            Repository.SetPriceAsync(product, new(Seller, size, null, unit, monthly, from));

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_new_price_ends_the_one_before_it_and_each_date_finds_the_price_it_was_sold_at()
    {
        using var store = new Store();
        var product = await store.ProductAsync("receptionist-setup", ProductKinds.Service, BillingPeriods.OneTime);
        var first = await store.PriceAsync(product.Id, Jan, 100m);
        var second = await store.PriceAsync(product.Id, Jul, 120m);
        Assert.True(first.IsSuccess && second.IsSuccess, first.Error ?? second.Error);

        var march = await store.Repository.GetPriceInEffectAsync(Seller, product.Id, null, new DateOnly(2026, 3, 1));
        var august = await store.Repository.GetPriceInEffectAsync(Seller, product.Id, null, new DateOnly(2026, 8, 1));
        Assert.Equal((100m, 120m), (march.Value!.UnitPrice, august.Value!.UnitPrice));
        Assert.Equal(Jul.AddDays(-1), march.Value.EffectiveTo);
        Assert.Equal(NotFound, (await store.Repository.GetPriceInEffectAsync(Seller, product.Id, null, new DateOnly(2025, 12, 31))).ErrorCode);

        var history = (await store.Repository.ListPricesAsync(Seller, product.Id, true)).Value!;
        Assert.Equal([Jul, Jan], history.Select(p => p.EffectiveFrom));
        Assert.Single((await store.Repository.ListPricesAsync(Seller, product.Id, false)).Value!);
    }

    private const string NotFound = CrmFailureCodes.NotFound;

    [Fact]
    public async Task A_price_cannot_start_on_or_before_an_existing_one_and_each_size_has_its_own_history()
    {
        using var store = new Store();
        var product = await store.ProductAsync("melio-workflow", ProductKinds.Implementation, BillingPeriods.OneTime);
        await store.PriceAsync(product.Id, Jul, 100m, size: EstimateSizes.Small);

        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.PriceAsync(product.Id, Jul, 90m, size: EstimateSizes.Small)).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.PriceAsync(product.Id, Jan, 90m, size: EstimateSizes.Small)).ErrorCode);
        Assert.True((await store.PriceAsync(product.Id, Jan, 400m, size: EstimateSizes.Large)).IsSuccess);
        Assert.True((await store.PriceAsync(product.Id, Jan, 50m)).IsSuccess);
        Assert.Equal(400m, (await store.Repository.GetPriceInEffectAsync(Seller, product.Id, EstimateSizes.Large, new DateOnly(2026, 8, 1))).Value!.UnitPrice);
        Assert.Equal(50m, (await store.Repository.GetPriceInEffectAsync(Seller, product.Id, null, new DateOnly(2026, 8, 1))).Value!.UnitPrice);
    }

    [Fact]
    public async Task A_line_item_without_a_price_uses_the_catalog_price_and_is_refused_when_none_is_set()
    {
        using var store = new Store();
        var saas = await store.ProductAsync("geek-receptionist");
        var setup = await store.ProductAsync("receptionist-setup", ProductKinds.Service, BillingPeriods.OneTime);
        var deal = (await store.Repository.CreateDealAsync(new(Seller, "Boca", "ai_receptionist", store.Organization, null, null, null, null, null))).Value!.Id;

        var unpriced = await store.Repository.AddLineItemAsync(deal, new(Seller, saas.Id, 1, null, null, 0));
        Assert.Equal(CrmFailureCodes.InvalidRequest, unpriced.ErrorCode);
        Assert.Contains("No monthly price is set", unpriced.Error, StringComparison.Ordinal);

        await store.PriceAsync(saas.Id, Jan, null, 49m);
        await store.PriceAsync(setup.Id, Jan, 297m);
        await store.PriceAsync(setup.Id, Jan, 500m, size: EstimateSizes.Large);
        var recurring = await store.Repository.AddLineItemAsync(deal, new(Seller, saas.Id, 1, null, null, 0));
        var once = await store.Repository.AddLineItemAsync(deal, new(Seller, setup.Id, 1, null, null, 0));
        var large = await store.Repository.AddLineItemAsync(deal, new(Seller, setup.Id, 1, null, null, 10, EstimateSizes.Large));
        Assert.True(recurring.IsSuccess && once.IsSuccess && large.IsSuccess, recurring.Error ?? once.Error ?? large.Error);
        Assert.Equal([49m, 297m, 500m], large.Value!.Items.Select(i => i.UnitPrice));
        Assert.Equal((747m, 49m), (large.Value.EstimatedValue, large.Value.RecurringValueMonthly));
        Assert.Equal(450m, large.Value.Items[2].LineTotal);

        var blank = await store.ProductAsync("blank-price", ProductKinds.Service, BillingPeriods.OneTime);
        await store.PriceAsync(blank.Id, Jan, null);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.AddLineItemAsync(deal, new(Seller, blank.Id, 1, null, null, 0))).ErrorCode);
    }

    [Fact]
    public async Task Pricing_rules_do_not_exist_until_set_and_setting_them_twice_keeps_one_row()
    {
        using var store = new Store();
        Assert.Equal(NotFound, (await store.Repository.GetPricingRulesAsync(Seller)).ErrorCode);

        await store.Repository.SetPricingRulesAsync(new(Seller, 150m, 60m));
        var again = await store.Repository.SetPricingRulesAsync(new(Seller, 175m, 55m));
        Assert.Equal((175m, 55m), (again.Value!.HourlyRate, again.Value.RelatedUseCasePct));
        Assert.Equal(1, await store.Db.PricingRules.CountAsync());
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.SetPricingRulesAsync(new(Seller, 150m, 120m))).ErrorCode);
        Assert.Equal(NotFound, (await store.Repository.GetPricingRulesAsync(Subscriber)).ErrorCode);
    }

    [Fact]
    public async Task Uplift_tiers_are_replaced_as_a_set_in_booked_order()
    {
        using var store = new Store();
        await store.Repository.ReplaceUpliftTiersAsync(new(Seller, [new(90m, 25m), new(75m, 10m)]));
        Assert.Equal([75m, 90m], (await store.Repository.ListUpliftTiersAsync(Seller)).Value!.Select(t => t.BookedPctFrom));

        await store.Repository.ReplaceUpliftTiersAsync(new(Seller, [new(80m, 15m)]));
        Assert.Equal([80m], (await store.Repository.ListUpliftTiersAsync(Seller)).Value!.Select(t => t.BookedPctFrom));
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.ReplaceUpliftTiersAsync(new(Seller, [new(80m, 15m), new(80m, 20m)]))).ErrorCode);
        Assert.Single((await store.Repository.ListUpliftTiersAsync(Seller)).Value!);
        Assert.Empty((await store.Repository.ListUpliftTiersAsync(Subscriber)).Value!);
    }

    private static CreateTenantSubscriptionCommand Subscribe(string code = "geek-receptionist", Guid? holding = null, Guid? tenant = null) =>
        new(tenant ?? Subscriber, code, "Starter", HoldingStatuses.Active, Jan, null, holding);

    [Fact]
    public async Task A_subscription_needs_an_existing_subscriber_and_an_active_geek_saas_product_of_the_seller()
    {
        using var store = new Store();
        await store.ProductAsync("geek-receptionist");
        await store.ProductAsync("receptionist-setup", ProductKinds.Service, BillingPeriods.OneTime);
        var retired = await store.ProductAsync("geek-social");
        await store.Repository.UpdateProductAsync(retired.Id, new(Seller, "Geek-Social", ProductCategories.Marketing, null, null, BillingPeriods.Monthly, false));

        Assert.True((await store.Repository.CreateSubscriptionAsync(Subscribe())).IsSuccess);
        Assert.Equal(NotFound, (await store.Repository.CreateSubscriptionAsync(Subscribe(tenant: Guid.NewGuid()))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateSubscriptionAsync(Subscribe("receptionist-setup"))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateSubscriptionAsync(Subscribe("geek-social"))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateSubscriptionAsync(Subscribe("no-such-product"))).ErrorCode);
    }

    [Fact]
    public async Task A_tenant_has_one_live_subscription_per_product_and_a_new_one_is_allowed_after_it_ends()
    {
        using var store = new Store();
        await store.ProductAsync("geek-receptionist");
        var first = await store.Repository.CreateSubscriptionAsync(Subscribe());
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.CreateSubscriptionAsync(Subscribe())).ErrorCode);

        var ended = await store.Repository.UpdateSubscriptionAsync(first.Value!.Id, new("Starter", HoldingStatuses.Ended, new DateOnly(2026, 6, 30)));
        Assert.Equal((HoldingStatuses.Ended, new DateOnly(2026, 6, 30)), (ended.Value!.Status, ended.Value.EndOn!.Value));
        Assert.True((await store.Repository.CreateSubscriptionAsync(Subscribe())).IsSuccess);
        Assert.Equal(2, (await store.Repository.ListSubscriptionsAsync(Subscriber)).Value!.Count);
        Assert.Empty((await store.Repository.ListSubscriptionsAsync(Guid.NewGuid())).Value!);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.UpdateSubscriptionAsync(first.Value.Id, new(null, HoldingStatuses.Ended, Jan.AddDays(-1)))).ErrorCode);
    }

    [Fact]
    public async Task A_subscription_links_the_sellers_own_holding_of_that_product_once()
    {
        using var store = new Store();
        var saas = await store.ProductAsync("geek-receptionist");
        var other = await store.ProductAsync("geek-social");
        var holding = (await store.Repository.CreateHoldingAsync(new(Seller, store.Organization, saas.Id,
            new(HoldingStatuses.Active, null, null, 1, 49, 0, 49, null, null, null, null, null, null, null)))).Value!;
        var wrong = (await store.Repository.CreateHoldingAsync(new(Seller, store.Organization, other.Id,
            new(HoldingStatuses.Active, null, null, 1, 49, 0, 49, null, null, null, null, null, null, null)))).Value!;

        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.CreateSubscriptionAsync(Subscribe(holding: wrong.Id))).ErrorCode);
        Assert.Equal(NotFound, (await store.Repository.CreateSubscriptionAsync(Subscribe(holding: Guid.NewGuid()))).ErrorCode);

        var made = await store.Repository.CreateSubscriptionAsync(Subscribe(holding: holding.Id));
        Assert.True(made.IsSuccess, made.Error);
        Assert.Equal(made.Value!.Id, (await store.Repository.ListHoldingsAsync(Seller, store.Organization)).Value!.Single(h => h.Id == holding.Id).TenantSubscriptionId);

        await store.Repository.UpdateSubscriptionAsync(made.Value.Id, new(null, HoldingStatuses.Cancelled, null));
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.CreateSubscriptionAsync(Subscribe(holding: holding.Id))).ErrorCode);
    }

    // ----- the platform-admin gate -----

    private static async Task<IActionResult?> GateAsync(string? configured, string? header)
    {
        var settings = new Dictionary<string, string?>();
        if (configured is not null) settings[PlatformAdminKeyAttribute.Variable] = configured;
        var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (header is not null) http.Request.Headers[PlatformAdminKeyAttribute.Header] = header;
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new PlatformAdminKeyAttribute().OnAuthorizationAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task The_platform_routes_are_closed_without_a_configured_key_and_open_only_to_the_right_one()
    {
        Assert.Equal(503, Assert.IsType<ObjectResult>(await GateAsync(null, "anything")).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await GateAsync("  ", "  ")).StatusCode);
        Assert.IsType<UnauthorizedObjectResult>(await GateAsync("secret", null));
        Assert.IsType<UnauthorizedObjectResult>(await GateAsync("secret", "wrong"));
        Assert.IsType<UnauthorizedObjectResult>(await GateAsync("secret", ""));
        Assert.Null(await GateAsync("secret", "secret"));
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
    public async Task Every_pricing_and_subscription_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var recorder = new Recorder();
        var client = new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance);
        var id = Guid.NewGuid();

        await client.ListPricesAsync(Seller, id, true);
        await client.GetPriceInEffectAsync(Seller, id, EstimateSizes.Large, Jan);
        await client.SetPriceAsync(id, new(Seller, null, null, 1m, null, Jan));
        await client.GetPricingRulesAsync(Seller);
        await client.SetPricingRulesAsync(new(Seller, 150m, 60m));
        await client.ListUpliftTiersAsync(Seller);
        await client.ReplaceUpliftTiersAsync(new(Seller, []));
        await client.ListSubscriptionsAsync(Subscriber);
        await client.CreateSubscriptionAsync(Subscribe());
        await client.UpdateSubscriptionAsync(id, new(null, HoldingStatuses.Active, null));

        Assert.Equal(10, recorder.Calls.Count);
        var routes = Routes(typeof(GeekRepository.Controllers.Crm.CrmController)).Concat(Routes(typeof(GeekRepository.Controllers.Platform.CrmPlatformController))).ToList();
        foreach (var call in recorder.Calls)
        {
            var path = call.PathAndQuery[..(call.PathAndQuery.IndexOf('?', StringComparison.Ordinal) is var q and >= 0 ? q : call.PathAndQuery.Length)].TrimStart('/');
            Assert.True(routes.Any(r => r.Verb == call.Method.Method && Matches(r.Template, path)), $"GeekRepository declares no {call.Method} route for {path}.");
        }
    }

    [Fact]
    public void GeekApi_exposes_the_pricing_paths_and_the_platform_paths_sit_behind_the_key()
    {
        var crm = Routes(typeof(GeekAPI.Controllers.Crm.CrmController)).Select(r => $"{r.Verb} {r.Template["api/crm/".Length..]}").ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
                 {
                     "GET products/{productId}/prices", "POST products/{productId}/prices", "GET products/{productId}/price",
                     "GET pricing-rules", "PUT pricing-rules", "GET pricing-uplift-tiers", "PUT pricing-uplift-tiers",
                 })
            Assert.Contains(expected, crm);

        var platform = Routes(typeof(CrmPlatformController)).Select(r => $"{r.Verb} {r.Template}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["GET api/platform/subscriptions", "POST api/platform/subscriptions", "PUT api/platform/subscriptions/{subscriptionId}"], platform.Order());
        Assert.NotNull(typeof(CrmPlatformController).GetCustomAttribute<PlatformAdminKeyAttribute>());
    }
}
