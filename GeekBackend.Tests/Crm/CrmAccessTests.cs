using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using GeekAPI.Auth;
using GeekAPI.Controllers.Crm;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Who may call /api/crm/*. A server holding the X-API-Key, or a signed-in GeekOAuth user with the geekcrm.manage scope
/// who belongs to a tenant; never a bare valid token. The tenant of a signed-in call is the one in tenant_users, whatever the
/// address says; a member cannot edit the catalog, pricing, property definitions or imports and cannot see sensitive
/// properties; membership is kept for at most a minute. These tests build the principal themselves, so the development
/// bearer shortcut in ApiKeyMiddleware (which carries no scope) cannot make one of them pass for the wrong reason.
/// </summary>
public class CrmAccessTests
{
    private static readonly Guid TenantA = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid TenantB = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2aff");
    private static readonly Guid UserOwner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserMember = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid UserStranger = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private sealed class Directory(Dictionary<Guid, CrmMembership> members)
    {
        public int Lookups { get; private set; }
        public bool Down { get; set; }
        public Dictionary<Guid, CrmMembership> Members { get; } = members;

        public Task<Result<CrmMembership>> LookupAsync(Guid user, CancellationToken ct)
        {
            Lookups++;
            if (Down) return Task.FromResult(Result<CrmMembership>.Failure("down", CrmFailureCodes.StoreUnavailable));
            return Task.FromResult(Members.TryGetValue(user, out var m)
                ? Result<CrmMembership>.Success(m)
                : Result<CrmMembership>.Failure("none", CrmFailureCodes.NotFound));
        }
    }

    private static Directory NewDirectory() => new(new()
    {
        [UserOwner] = new CrmMembership(TenantA, UserOwner, TenantUserRoles.Owner),
        [UserMember] = new CrmMembership(TenantB, UserMember, TenantUserRoles.Member),
    });

    private static (CrmMembershipCache Cache, FakeTimeProvider Clock) CacheOf(Directory directory)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
        return (new CrmMembershipCache(directory.LookupAsync, clock), clock);
    }

    private static async Task<(IActionResult? Result, CrmCaller? Caller)> RunAsync(CrmMembershipCache cache, ClaimsPrincipal? user, bool apiKey = false, bool ownerOnly = false)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(cache).BuildServiceProvider(),
        };
        if (user is not null) http.User = user;
        if (apiKey) http.Items[CrmAuthConstants.VerifiedApiKeyItem] = true;
        var descriptor = new ActionDescriptor();
        if (ownerOnly) descriptor.EndpointMetadata = [new CrmOwnerOnlyAttribute()];
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
        await new CrmAccessAttribute().OnAuthorizationAsync(context);
        return (context.Result, http.Items.TryGetValue(CrmAuthConstants.CallerItem, out var c) ? c as CrmCaller : null);
    }

    private static ClaimsPrincipal Token(Guid? sub, string scope = "openid profile geekcrm.manage")
    {
        var identity = new ClaimsIdentity("Bearer");
        if (sub is { } id) identity.AddClaim(new Claim("sub", id.ToString()));
        if (scope.Length > 0) identity.AddClaim(new Claim("scope", scope));
        return new ClaimsPrincipal(identity);
    }

    private static int Status(IActionResult? result) => Assert.IsType<ObjectResult>(result).StatusCode!.Value;

    // ----- who gets in -----

    [Fact]
    public async Task A_server_with_the_verified_api_key_is_trusted_with_every_tenant_and_role()
    {
        var (cache, _) = CacheOf(NewDirectory());
        var (result, caller) = await RunAsync(cache, null, apiKey: true, ownerOnly: true);
        Assert.Null(result);
        Assert.True(caller!.IsServer && caller.IsOwner);
    }

    [Fact]
    public async Task A_request_with_no_key_and_no_token_is_unauthenticated()
    {
        var (cache, _) = CacheOf(NewDirectory());
        Assert.Equal(401, Status((await RunAsync(cache, null)).Result));
        Assert.Equal(401, Status((await RunAsync(cache, new ClaimsPrincipal(new ClaimsIdentity()))).Result));
    }

    [Theory]
    [InlineData("openid profile")]
    [InlineData("content-creator.manage geeksocial.manage")]
    [InlineData("geekcrm.manager")]
    [InlineData("")]
    public async Task A_valid_token_without_the_geekcrm_scope_is_forbidden_even_for_a_real_member(string scope)
    {
        var directory = NewDirectory();
        var (cache, _) = CacheOf(directory);
        var (result, caller) = await RunAsync(cache, Token(UserOwner, scope));
        Assert.Equal(403, Status(result));
        Assert.Null(caller);
        Assert.Equal(0, directory.Lookups);
    }

    [Fact]
    public async Task A_user_with_the_scope_but_no_tenant_row_is_forbidden()
    {
        var (cache, _) = CacheOf(NewDirectory());
        Assert.Equal(403, Status((await RunAsync(cache, Token(UserStranger))).Result));
        Assert.Equal(403, Status((await RunAsync(cache, Token(null))).Result));
        Assert.Equal(403, Status((await RunAsync(cache, Token(Guid.Empty))).Result));
    }

    [Fact]
    public async Task A_member_signs_in_as_their_tenant_and_role_and_a_failed_lookup_is_unavailable_not_forbidden()
    {
        var directory = NewDirectory();
        var (cache, _) = CacheOf(directory);
        var (result, caller) = await RunAsync(cache, Token(UserMember));
        Assert.Null(result);
        Assert.Equal((false, TenantB, TenantUserRoles.Member, UserMember), (caller!.IsServer, caller.TenantId!.Value, caller.Role, caller.UserId!.Value));

        directory.Down = true;
        var (down, _) = CacheOf(directory);
        Assert.Equal(503, Status((await RunAsync(down, Token(UserOwner))).Result));
    }

    // ----- the role matrix -----

    [Fact]
    public async Task A_member_is_refused_an_owner_only_route_and_an_owner_is_not()
    {
        var (cache, _) = CacheOf(NewDirectory());
        Assert.Equal(403, Status((await RunAsync(cache, Token(UserMember), ownerOnly: true)).Result));
        Assert.Null((await RunAsync(cache, Token(UserOwner), ownerOnly: true)).Result);
        Assert.Null((await RunAsync(cache, Token(UserMember), ownerOnly: false)).Result);
    }

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(CrmController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void The_owner_only_routes_are_exactly_the_catalog_pricing_property_definitions_and_imports()
    {
        var ownerOnly = Actions().Where(m => m.GetCustomAttribute<CrmOwnerOnlyAttribute>() is not null).Select(m => m.Name).Order().ToArray();
        Assert.Equal(
            ["CreateProduct", "CreatePropertyDefinition", "ImportApollo", "ReplaceUpliftTiers", "RetirePropertyDefinition", "SetPrice", "SetPricingRules", "UpdateProduct", "UpdatePropertyDefinition"],
            ownerOnly);
    }

    [Fact]
    public void Every_crm_route_sits_behind_the_access_filter()
    {
        Assert.NotNull(typeof(CrmController).GetCustomAttribute<CrmAccessAttribute>());
        Assert.NotEmpty(Actions());
    }

    [Fact]
    public void A_member_can_work_leads_and_deals_holdings_projects_and_read_the_catalog()
    {
        var open = Actions().Where(m => m.GetCustomAttribute<CrmOwnerOnlyAttribute>() is null).Select(m => m.Name).ToHashSet();
        foreach (var name in new[] { "ListProducts", "ListPrices", "GetPriceInEffect", "GetPricingRules", "ListUpliftTiers", "ListParticipants", "ListDeals", "AddLineItem", "WinDeal", "CreateHolding", "CreateProject", "ListProjects", "GetCapacity", "ReceptionistIntake" })
            Assert.Contains(name, open);
    }

    // ----- the tenant is the token's, never the address's -----

    /// <summary>Stands in for the store: every call stops at once and reports which store method was reached and with what.</summary>
    private sealed class StoreReached(string method, object?[] args) : Exception(method)
    {
        public string Method { get; } = method;
        public object?[] Args { get; } = args;
    }

    private class Trap : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new StoreReached(targetMethod!.Name, args ?? []);
    }

    private static CrmController ControllerFor(CrmCaller caller)
    {
        var controller = new CrmController(DispatchProxy.Create<GeekApplication.Interfaces.Crm.ICrmStore, Trap>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Items[CrmAuthConstants.CallerItem] = caller;
        return controller;
    }

    private static async Task<StoreReached> ReachedAsync(Func<Task<IActionResult>> call) =>
        await Assert.ThrowsAsync<StoreReached>(call);

    [Fact]
    public async Task A_signed_in_user_is_held_to_their_tenant_whatever_the_address_asks_for_and_a_server_chooses_its_own()
    {
        var member = new CrmCaller(false, TenantB, TenantUserRoles.Member, UserMember);

        var asked = await ReachedAsync(() => ControllerFor(member).ListDeals(false, 10, TenantA, default));
        Assert.Equal(("ListDealsAsync", TenantB), (asked.Method, asked.Args[0]));
        var none = await ReachedAsync(() => ControllerFor(member).ListDeals(false, 10, null, default));
        Assert.Equal(TenantB, none.Args[0]);

        var named = await ReachedAsync(() => ControllerFor(CrmCaller.Server).ListDeals(false, 10, TenantB, default));
        Assert.Equal(TenantB, named.Args[0]);
        var defaulted = await ReachedAsync(() => ControllerFor(CrmCaller.Server).ListDeals(false, 10, null, default));
        Assert.Equal(TenantA, defaulted.Args[0]);
    }

    [Fact]
    public async Task A_member_never_sees_sensitive_properties_and_an_owner_only_when_they_ask()
    {
        var member = new CrmCaller(false, TenantB, TenantUserRoles.Member, UserMember);
        var owner = new CrmCaller(false, TenantA, TenantUserRoles.Owner, UserOwner);

        // ListPropertyDefinitionsAsync(tenantId, objectType, includeRetired, includeSensitive, ct)
        static bool Sensitive(StoreReached reached) => (bool)reached.Args[3]!;

        Assert.False(Sensitive(await ReachedAsync(() => ControllerFor(member).ListPropertyDefinitions(null, false, true, null, default))));
        Assert.True(Sensitive(await ReachedAsync(() => ControllerFor(owner).ListPropertyDefinitions(null, false, true, null, default))));
        Assert.False(Sensitive(await ReachedAsync(() => ControllerFor(owner).ListPropertyDefinitions(null, false, false, null, default))));
        Assert.True(Sensitive(await ReachedAsync(() => ControllerFor(CrmCaller.Server).ListPropertyDefinitions(null, false, true, null, default))));
    }

    [Fact]
    public void Me_says_who_is_calling_from_the_caller_the_filter_set()
    {
        var member = Assert.IsType<OkObjectResult>(ControllerFor(new CrmCaller(false, TenantB, TenantUserRoles.Member, UserMember)).Me());
        Assert.Equal(new CrmMe(false, TenantB, TenantUserRoles.Member, UserMember), member.Value);

        var server = Assert.IsType<OkObjectResult>(ControllerFor(CrmCaller.Server).Me());
        Assert.Equal(new CrmMe(true, null, TenantUserRoles.Owner, null), server.Value);
    }

    [Fact]
    public async Task A_controller_the_filter_did_not_run_for_refuses_rather_than_guessing_a_tenant()
    {
        var controller = new CrmController(DispatchProxy.Create<GeekApplication.Interfaces.Crm.ICrmStore, Trap>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ListDeals(false, 10, null, default));
    }

    // ----- membership is kept for at most a minute -----

    [Fact]
    public async Task Membership_is_kept_for_a_minute_and_a_removed_member_loses_access_after_it()
    {
        var directory = NewDirectory();
        var (cache, clock) = CacheOf(directory);

        Assert.Null((await RunAsync(cache, Token(UserMember))).Result);
        Assert.Null((await RunAsync(cache, Token(UserMember))).Result);
        Assert.Equal(1, directory.Lookups);

        directory.Members.Remove(UserMember);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Null((await RunAsync(cache, Token(UserMember))).Result);
        Assert.Equal(1, directory.Lookups);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(403, Status((await RunAsync(cache, Token(UserMember))).Result));
        Assert.Equal(2, directory.Lookups);
    }

    [Fact]
    public async Task A_missing_or_failed_lookup_is_never_remembered()
    {
        var directory = NewDirectory();
        var (cache, _) = CacheOf(directory);
        Assert.Equal(403, Status((await RunAsync(cache, Token(UserStranger))).Result));
        directory.Members[UserStranger] = new CrmMembership(TenantB, UserStranger, TenantUserRoles.Member);
        Assert.Null((await RunAsync(cache, Token(UserStranger))).Result);

        directory.Down = true;
        directory.Members.Remove(UserOwner);
        Assert.Equal(503, Status((await RunAsync(cache, Token(UserOwner))).Result));
        directory.Down = false;
        Assert.Equal(403, Status((await RunAsync(cache, Token(UserOwner))).Result));
    }

    // ----- the api key says so, once, in the middleware -----

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "GeekAPI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task Only_a_request_with_the_right_api_key_is_marked_verified()
    {
        var previous = Environment.GetEnvironmentVariable("GEEK_BACKEND_API_KEY");
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "right-key");
        try
        {
            async Task<(bool Reached, bool? Verified)> CallAsync(string? key)
            {
                var reached = false;
                var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new TestEnvironment("Production"));
                var context = new DefaultHttpContext();
                context.Request.Path = "/api/crm/deals";
                if (key is not null) context.Request.Headers["X-API-Key"] = key;
                await middleware.InvokeAsync(context);
                return (reached, context.Items.TryGetValue(CrmAuthConstants.VerifiedApiKeyItem, out var v) ? v as bool? : null);
            }

            Assert.Equal((true, true), await CallAsync("right-key"));
            Assert.Equal((false, null), await CallAsync("wrong-key"));
            Assert.Equal((false, null), await CallAsync(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", previous);
        }
    }
}
