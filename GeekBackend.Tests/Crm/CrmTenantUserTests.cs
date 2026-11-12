using System.Net;
using System.Reflection;
using System.Text;
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
/// tenant_users: which tenant a signed-in GeekOAuth user works in and as what. A user belongs to one tenant; a tenant
/// always keeps an owner; an unknown user has no membership (and so no access). The store runs in memory; the unique
/// index on user_id and the platform-only row-level security are proved by the integration tests against Postgres.
/// </summary>
public class CrmTenantUserTests
{
    private static readonly Guid TenantA = Guid.Parse("a0a0a0a0-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("b0b0b0b0-0000-4000-8000-000000000002");
    private static readonly Guid Alice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Carol = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("crm-users-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.AddRange(new CrmTenantRow { Id = TenantA, Name = "Geek", Slug = "geek" }, new CrmTenantRow { Id = TenantB, Name = "Boca Dental", Slug = "boca" });
            Db.SaveChanges();
        }

        public async Task<CrmTenantUser> AddAsync(Guid tenant, Guid user, string role)
        {
            var made = await Repository.AddTenantUserAsync(new AddTenantUserCommand(tenant, user, role));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_user_with_no_row_has_no_membership_and_a_member_is_found_by_their_user_id()
    {
        using var store = new Store();
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.GetMembershipAsync(Alice)).ErrorCode);

        await store.AddAsync(TenantA, Alice, TenantUserRoles.Owner);
        await store.AddAsync(TenantB, Bob, TenantUserRoles.Member);
        Assert.Equal(new CrmMembership(TenantA, Alice, TenantUserRoles.Owner), (await store.Repository.GetMembershipAsync(Alice)).Value);
        Assert.Equal(new CrmMembership(TenantB, Bob, TenantUserRoles.Member), (await store.Repository.GetMembershipAsync(Bob)).Value);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.GetMembershipAsync(Carol)).ErrorCode);
    }

    [Fact]
    public async Task A_user_belongs_to_one_tenant_and_a_bad_role_user_or_tenant_is_refused()
    {
        using var store = new Store();
        await store.AddAsync(TenantA, Alice, TenantUserRoles.Owner);

        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.AddTenantUserAsync(new(TenantB, Alice, TenantUserRoles.Member))).ErrorCode);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.AddTenantUserAsync(new(TenantA, Alice, TenantUserRoles.Member))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.AddTenantUserAsync(new(TenantA, Bob, "admin"))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.AddTenantUserAsync(new(TenantA, Guid.Empty, TenantUserRoles.Member))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.AddTenantUserAsync(new(Guid.NewGuid(), Bob, TenantUserRoles.Member))).ErrorCode);
        Assert.Single((await store.Repository.ListTenantUsersAsync(null)).Value!);
    }

    [Fact]
    public async Task Listing_can_be_narrowed_to_one_tenant()
    {
        using var store = new Store();
        await store.AddAsync(TenantA, Alice, TenantUserRoles.Owner);
        await store.AddAsync(TenantB, Bob, TenantUserRoles.Owner);
        await store.AddAsync(TenantB, Carol, TenantUserRoles.Member);

        Assert.Equal(3, (await store.Repository.ListTenantUsersAsync(null)).Value!.Count);
        Assert.Equal([Bob, Carol], (await store.Repository.ListTenantUsersAsync(TenantB)).Value!.Select(u => u.UserId).Order());
        Assert.Equal([Alice], (await store.Repository.ListTenantUsersAsync(TenantA)).Value!.Select(u => u.UserId));
    }

    [Fact]
    public async Task A_tenant_keeps_its_last_owner_whether_the_owner_is_demoted_or_removed()
    {
        using var store = new Store();
        var alice = await store.AddAsync(TenantA, Alice, TenantUserRoles.Owner);
        var bob = await store.AddAsync(TenantA, Bob, TenantUserRoles.Member);

        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.SetTenantUserRoleAsync(alice.Id, new(TenantUserRoles.Member))).ErrorCode);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.RemoveTenantUserAsync(alice.Id)).ErrorCode);
        Assert.Equal(TenantUserRoles.Owner, (await store.Repository.GetMembershipAsync(Alice)).Value!.Role);

        Assert.Equal(TenantUserRoles.Owner, (await store.Repository.SetTenantUserRoleAsync(bob.Id, new(TenantUserRoles.Owner))).Value!.Role);
        Assert.Equal(TenantUserRoles.Member, (await store.Repository.SetTenantUserRoleAsync(alice.Id, new(TenantUserRoles.Member))).Value!.Role);
        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.RemoveTenantUserAsync(bob.Id)).ErrorCode);
    }

    [Fact]
    public async Task Another_tenants_owner_does_not_count_and_a_removed_member_loses_their_membership()
    {
        using var store = new Store();
        var alice = await store.AddAsync(TenantA, Alice, TenantUserRoles.Owner);
        await store.AddAsync(TenantB, Bob, TenantUserRoles.Owner);
        var carol = await store.AddAsync(TenantA, Carol, TenantUserRoles.Member);

        Assert.Equal(CrmFailureCodes.Conflict, (await store.Repository.RemoveTenantUserAsync(alice.Id)).ErrorCode);
        Assert.True((await store.Repository.RemoveTenantUserAsync(carol.Id)).IsSuccess);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.GetMembershipAsync(Carol)).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.RemoveTenantUserAsync(carol.Id)).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.SetTenantUserRoleAsync(carol.Id, new(TenantUserRoles.Owner))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.SetTenantUserRoleAsync(alice.Id, new("admin"))).ErrorCode);
    }

    [Fact]
    public void The_roles_are_exactly_owner_and_member()
    {
        Assert.Equal(["member", "owner"], TenantUserRoles.All.Order());
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

    [Fact]
    public async Task Every_membership_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var recorder = new Recorder();
        var client = new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance);
        var id = Guid.NewGuid();

        await client.GetMembershipAsync(Alice);
        await client.ListTenantUsersAsync(TenantA);
        await client.ListTenantUsersAsync(null);
        await client.AddTenantUserAsync(new(TenantA, Alice, TenantUserRoles.Owner));
        await client.SetTenantUserRoleAsync(id, new(TenantUserRoles.Member));
        await client.RemoveTenantUserAsync(id);

        Assert.Equal(6, recorder.Calls.Count);
        var controller = typeof(GeekRepository.Controllers.Platform.CrmPlatformController);
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        var routes = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => (Verb: a.HttpMethods.Single(), Template: System.Text.RegularExpressions.Regex.Replace($"{root}/{a.Template}", @"\{(\w+):[^}]+\}", "{$1}")))
            .ToList();
        foreach (var call in recorder.Calls)
        {
            var path = call.PathAndQuery.Split('?')[0].TrimStart('/').Split('/');
            Assert.True(
                routes.Any(r => r.Verb == call.Method.Method && r.Template.Split('/') is var t && t.Length == path.Length && t.Zip(path).All(p => p.First.StartsWith('{') || p.First == p.Second)),
                $"GeekRepository declares no {call.Method} route for {string.Join('/', path)}.");
        }
        Assert.Contains(recorder.Calls, c => c.PathAndQuery.Contains($"tenantId={TenantA}", StringComparison.Ordinal));
    }
}
