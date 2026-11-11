using System.Reflection;
using GeekAPI.Auth;
using GeekAPI.Controllers.Social;
using GeekAPI.Controllers.Social.Hubs;
using GeekAPI.Services.Social;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.Listener;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Publishing;
using GeekAPI.Services.Social.Realtime;
using GeekAPI.Services.Social.Sending;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The screen's side of GeekAPI: every api/social route but the webhooks, and the hub, need
/// geeksocial.manage; each failure code becomes one status; everything the routes reach resolves; the
/// hosted orchestrator and the one the routes call are the same instance; and a live update that
/// cannot be sent is logged, never thrown into the change it reports.
/// </summary>
public class SocialApiTests
{
    [Fact]
    public void Every_screen_route_and_the_hub_need_the_geeksocial_scope()
    {
        var onController = typeof(SocialController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(GeekSocialAuthConstants.ManagePolicy, onController!.Policy);
        Assert.Equal("api/social", typeof(SocialController).GetCustomAttribute<RouteAttribute>()!.Template);

        var anonymous = typeof(SocialController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
        Assert.Empty(anonymous);

        Assert.Equal(GeekSocialAuthConstants.ManagePolicy, typeof(SocialRealtimeHub).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("geeksocial.manage", GeekSocialAuthConstants.ManageScope);
    }

    [Theory]
    [InlineData(SocialFailureCodes.InvalidRequest, 400)]
    [InlineData(SocialFailureCodes.Forbidden, 403)]
    [InlineData(SocialFailureCodes.NotFound, 404)]
    [InlineData(SocialFailureCodes.Conflict, 409)]
    [InlineData(SocialFailureCodes.TokenInvalid, 409)]
    [InlineData(SocialFailureCodes.ClearanceRequired, 409)]
    [InlineData(SocialFailureCodes.NotSupported, 422)]
    [InlineData(SocialFailureCodes.RateLimited, 429)]
    [InlineData(SocialFailureCodes.StoreUnavailable, 503)]
    [InlineData(SocialFailureCodes.ModelFailed, 503)]
    [InlineData(null, 503)]
    public void A_failure_code_becomes_one_status(string? code, int expected) =>
        Assert.Equal(expected, SocialController.StatusFor(code));

    [Fact]
    public void Everything_the_routes_and_the_orchestrator_reach_resolves_and_there_is_one_orchestrator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IHubContext<SocialRealtimeHub>>(new ThrowingHubContext());
        services.AddSingleton(new LlmConcurrencyGate(1));
        services.AddHttpClient("GeekRepository", c => c.BaseAddress = new Uri("http://geek-repository.test/"));
        services.AddSocialPersistence();
        services.AddSocialIntake();
        services.AddSocialAgents();
        services.AddSocialSending();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SocialOperatorActions>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SocialConnectService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SocialResync>());
        Assert.IsType<SocialSendStep>(scope.ServiceProvider.GetRequiredService<ISocialSendStep>());
        Assert.IsType<PostWriter>(scope.ServiceProvider.GetRequiredService<IPostWriter>());
        Assert.IsType<MetaPublisher>(scope.ServiceProvider.GetRequiredKeyedService<ISocialPublisher>(SocialPlatforms.Facebook));
        Assert.IsType<MetaEngagementWriter>(scope.ServiceProvider.GetRequiredKeyedService<ISocialEngagementWriter>(SocialPlatforms.Facebook));
        Assert.IsType<MetaAccountConnector>(scope.ServiceProvider.GetRequiredKeyedService<ISocialAccountConnector>(SocialPlatforms.Facebook));
        Assert.IsType<SocialRealtimeNotifier>(provider.GetRequiredService<ISocialRealtime>());

        var hosted = provider.GetServices<IHostedService>().OfType<SocialOrchestrator>().Single();
        Assert.Same(hosted, provider.GetRequiredService<SocialOrchestrator>());
    }

    private sealed class ThrowingHubContext : IHubContext<SocialRealtimeHub>
    {
        public IHubClients Clients { get; } = new ThrowingClients();
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class ThrowingClients : IHubClients
        {
            public IClientProxy All => new ThrowingProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class ThrowingProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("The hub is down.");
        }
    }

    [Fact]
    public async Task A_live_update_that_cannot_be_sent_is_logged_and_does_not_fail_the_change()
    {
        var log = new RecordingLog<SocialRealtimeNotifier>();
        var notifier = new SocialRealtimeNotifier(new ThrowingHubContext(), log);

        await notifier.DraftChangedAsync(Guid.NewGuid(), Guid.NewGuid(), ReplyDraftStatuses.Sent);

        var line = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.NotNull(line.Exception);
    }
}
