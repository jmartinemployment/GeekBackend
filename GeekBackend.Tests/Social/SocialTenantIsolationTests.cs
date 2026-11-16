using System.Net;
using System.Security.Claims;
using GeekAPI.Controllers.Social.Hubs;
using GeekAPI.HttpClients;
using GeekAPI.Services.Social.Realtime;
using GeekApplication.Models.Social;
using GeekRepository.Controllers.Social;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Social;

/// <summary>
/// One tenant never reaches another's data: the repository routes refuse a call with no tenant, the HTTP client
/// always names one, a live message goes to its tenant's group and to no one else, and a connection with no tenant
/// is refused instead of listening to everything.
/// </summary>
public class SocialTenantIsolationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    // ----- Live messages -----

    private sealed class CapturingHub : IHubContext<SocialRealtimeHub>
    {
        public List<string> GroupsAddressed { get; } = new();
        public List<(string Group, string Method)> Sent { get; } = new();
        public bool AllWasUsed { get; private set; }

        public IHubClients Clients => new CapturingClients(this);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class CapturingClients(CapturingHub hub) : IHubClients
        {
            public IClientProxy All { get { hub.AllWasUsed = true; return new Proxy(hub, "<all>"); } }
            public IClientProxy Group(string groupName) { hub.GroupsAddressed.Add(groupName); return new Proxy(hub, groupName); }
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class Proxy(CapturingHub hub, string group) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                hub.Sent.Add((group, method));
                return Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task Every_live_message_goes_to_its_own_tenants_group_and_never_to_all()
    {
        var hub = new CapturingHub();
        var notifier = new SocialRealtimeNotifier(hub, NullLogger<SocialRealtimeNotifier>.Instance);

        await notifier.DraftChangedAsync(TenantA, Guid.NewGuid(), Guid.NewGuid(), ReplyDraftStatuses.Pending);
        await notifier.CommentChangedAsync(TenantB, Guid.NewGuid(), Guid.NewGuid(), TriageOutcomes.ReplyNeeded);
        await notifier.AccountChangedAsync(TenantA, Guid.NewGuid(), SocialAccountStatuses.Connected);

        Assert.False(hub.AllWasUsed);
        Assert.Equal(
            new[]
            {
                (SocialRealtimeHub.TenantGroup(TenantA), SocialRealtimeNotifier.DraftChanged),
                (SocialRealtimeHub.TenantGroup(TenantB), SocialRealtimeNotifier.CommentChanged),
                (SocialRealtimeHub.TenantGroup(TenantA), SocialRealtimeNotifier.AccountChanged),
            },
            hub.Sent.ToArray());
    }

    [Fact]
    public void Two_tenants_never_share_a_group()
    {
        Assert.NotEqual(SocialRealtimeHub.TenantGroup(TenantA), SocialRealtimeHub.TenantGroup(TenantB));
    }

    private sealed class FakeCallerContext(ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class FakeGroups : IGroupManager
    {
        public List<(string ConnectionId, string Group)> Added { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Person(string? subject) =>
        new(new ClaimsIdentity(subject is null ? Array.Empty<Claim>() : new[] { new Claim("sub", subject) }, "test"));

    [Fact]
    public async Task A_connection_joins_the_group_of_the_tenant_its_person_owns_and_no_other()
    {
        var owner = Guid.NewGuid();
        var store = new FakeSocialStore
        {
            OnGetTenantByOwner = id => GeekApplication.Results.Result<SocialTenant>.Success(new SocialTenant { Id = TenantA, OwnerUserId = id, Name = "A" }),
        };
        var groups = new FakeGroups();
        var hub = new SocialRealtimeHub(store, NullLogger<SocialRealtimeHub>.Instance)
        {
            Context = new FakeCallerContext(Person(owner.ToString())),
            Groups = groups,
        };

        await hub.OnConnectedAsync();

        Assert.Equal(("connection-1", SocialRealtimeHub.TenantGroup(TenantA)), Assert.Single(groups.Added));
    }

    [Fact]
    public async Task A_connection_from_a_person_with_no_tenant_is_refused_and_joins_nothing()
    {
        var store = new FakeSocialStore
        {
            OnGetTenantByOwner = _ => GeekApplication.Results.Result<SocialTenant>.Failure("No tenant for this person.", SocialFailureCodes.NotFound),
        };
        var groups = new FakeGroups();
        var hub = new SocialRealtimeHub(store, NullLogger<SocialRealtimeHub>.Instance)
        {
            Context = new FakeCallerContext(Person(Guid.NewGuid().ToString())),
            Groups = groups,
        };

        var refused = await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());

        Assert.Contains("Sign up", refused.Message);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task A_connection_whose_tenant_cannot_be_looked_up_is_refused_with_the_failure_and_joins_nothing()
    {
        var store = new FakeSocialStore
        {
            OnGetTenantByOwner = _ => GeekApplication.Results.Result<SocialTenant>.Failure("The social store failed.", SocialFailureCodes.StoreUnavailable),
        };
        var groups = new FakeGroups();
        var hub = new SocialRealtimeHub(store, NullLogger<SocialRealtimeHub>.Instance)
        {
            Context = new FakeCallerContext(Person(Guid.NewGuid().ToString())),
            Groups = groups,
        };

        var refused = await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());

        Assert.DoesNotContain("Sign up", refused.Message);
        Assert.Empty(groups.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_connection_whose_token_has_no_user_id_is_refused_before_any_lookup(string? subject)
    {
        var store = new FakeSocialStore();
        var groups = new FakeGroups();
        var hub = new SocialRealtimeHub(store, NullLogger<SocialRealtimeHub>.Instance)
        {
            Context = new FakeCallerContext(Person(subject)),
            Groups = groups,
        };

        await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());

        Assert.Empty(groups.Added);
    }

    // ----- GeekAPI to GeekRepository -----

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (HttpSocialRepository Repo, RecordingHandler Handler) Client()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://geek-repository.test/") };
        return (new HttpSocialRepository(http, NullLogger<HttpSocialRepository>.Instance), handler);
    }

    [Fact]
    public async Task A_tenant_scoped_call_carries_the_tenant_in_a_header()
    {
        var (repo, handler) = Client();

        await repo.ListDraftsAsync(TenantA, null, null, 10);
        await repo.ListAccountsAsync(TenantB);

        Assert.Equal(
            new[] { TenantA.ToString(), TenantB.ToString() },
            handler.Requests.Select(r => Assert.Single(r.Headers.GetValues(HttpSocialRepository.TenantHeader))).ToArray());
    }

    [Fact]
    public async Task A_tenant_scoped_call_with_an_empty_tenant_is_refused_and_never_sent()
    {
        var (repo, handler) = Client();

        var refused = await repo.ListAccountsAsync(Guid.Empty);

        Assert.False(refused.IsSuccess);
        Assert.Equal(SocialFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_platform_call_names_no_tenant()
    {
        var (repo, handler) = Client();

        await repo.ListUntriagedEventsAsync(10);
        await repo.ListDraftsAcrossTenantsAsync(ReplyDraftStatuses.Approved, 10);

        Assert.All(handler.Requests, r => Assert.False(r.Headers.Contains(HttpSocialRepository.TenantHeader)));
    }

    // ----- GeekRepository routes -----

    private static T WithHeader<T>(T controller, string? header) where T : ControllerBase
    {
        var http = new DefaultHttpContext();
        if (header is not null) http.Request.Headers["X-Social-Tenant-Id"] = header;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_route_called_without_a_usable_tenant_is_a_400_and_never_reaches_the_store(string? header)
    {
        var store = new FakeSocialStore();
        var drafts = WithHeader(new SocialDraftsController(store), header);
        var accounts = WithHeader(new SocialAccountsController(store), header);

        Assert.IsType<BadRequestObjectResult>(await drafts.List(null, null, 0, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await drafts.Get(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await accounts.List(CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await accounts.GetCredential(Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(store.TenantsSeen);
    }

    [Fact]
    public async Task A_route_asks_the_store_for_exactly_the_tenant_in_its_header()
    {
        var store = new FakeSocialStore
        {
            OnListDrafts = (_, _, _) => GeekApplication.Results.Result<IReadOnlyList<ReplyDraft>>.Success(new List<ReplyDraft>()),
        };
        var drafts = WithHeader(new SocialDraftsController(store), TenantB.ToString());

        Assert.IsType<OkObjectResult>(await drafts.List(null, null, 0, CancellationToken.None));

        Assert.Equal(TenantB, Assert.Single(store.TenantsSeen));
    }
}
