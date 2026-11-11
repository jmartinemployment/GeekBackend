using System.Security.Cryptography;
using GeekAPI.Services.Social;
using GeekAPI.Services.Social.Connector;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The connect flow: a state only the user who started it can finish, for the same address, within its
/// lifetime; each granted account stored with its token encrypted, audited and announced; a webhook
/// subscription that fails is shown with the account and logged, not hidden.
/// </summary>
public class SocialConnectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Uri Redirect = new("https://geek-social.geekatyourspot.com/connect/meta/callback");
    private static readonly Uri Webhook = new("https://api.geekatyourspot.com/api/social/webhooks/meta");

    private sealed class FakeConnector : ISocialAccountConnector
    {
        public Func<Result<SocialWebhookSubscription>> OnSubscribe { get; set; } =
            () => Result<SocialWebhookSubscription>.Success(new SocialWebhookSubscription { Active = true, Fields = new List<string> { "feed" } });

        public string? LastState { get; private set; }
        public int Exchanges { get; private set; }

        public string Platform => SocialPlatforms.Facebook;

        public Result<Uri> BuildAuthorizationUrl(string state, Uri redirectUri)
        {
            LastState = state;
            return Result<Uri>.Success(new Uri($"https://www.facebook.test/dialog?state={Uri.EscapeDataString(state)}"));
        }

        public Task<Result<SocialConnection>> ExchangeAuthorizationCodeAsync(string code, Uri redirectUri, CancellationToken ct = default)
        {
            Exchanges++;
            return Task.FromResult(Result<SocialConnection>.Success(new SocialConnection
            {
                Accounts =
                {
                    new SocialConnectedAccount
                    {
                        Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1", DisplayName = "Geek @ Your Spot",
                        Token = new SocialToken("PRIVATE-PAGE-TOKEN"),
                    },
                },
            }));
        }

        public Task<Result<SocialToken>> RefreshTokenAsync(SocialToken token, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not used.");

        public Task<Result<SocialTokenStatus>> GetTokenStatusAsync(SocialToken token, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not used.");

        public Task<Result<SocialWebhookSubscription>> EnsureWebhookSubscriptionAsync(SocialAccount account, SocialToken token, Uri callbackUrl, CancellationToken ct = default) =>
            Task.FromResult(OnSubscribe());
    }

    private sealed class Rig
    {
        public required SocialConnectService Service { get; init; }
        public required FakeConnector Connector { get; init; }
        public required FakeSocialStore Store { get; init; }
        public required FakeRealtime Realtime { get; init; }
        public required RecordingLog<SocialConnectService> Log { get; init; }
        public List<ConnectSocialAccountCommand> Connected { get; } = new();
    }

    private static Rig Build(TimeProvider? clock = null)
    {
        var connector = new FakeConnector();
        var store = new FakeSocialStore { OnRecordAudit = a => Result<ActionAudit>.Success(a) };
        var services = new ServiceCollection().AddKeyedSingleton<ISocialAccountConnector>(SocialPlatforms.Facebook, connector).BuildServiceProvider();
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var realtime = new FakeRealtime();
        var log = new RecordingLog<SocialConnectService>();
        var rig = new Rig
        {
            Service = new SocialConnectService(services, store, new SocialCredentialProtector(() => key),
                new EphemeralDataProtectionProvider(), realtime, clock ?? new MetaEngagementReaderTests.FixedClock(Now), log),
            Connector = connector,
            Store = store,
            Realtime = realtime,
            Log = log,
        };
        store.OnConnectAccount = command =>
        {
            rig.Connected.Add(command);
            return Result<SocialAccount>.Success(new SocialAccount
            {
                Id = Guid.NewGuid(), Platform = command.Platform, PlatformAccountId = command.PlatformAccountId,
                DisplayName = command.DisplayName, Status = SocialAccountStatuses.Connected,
            });
        };
        return rig;
    }

    private static string StartedState(Rig rig, string user = "user-1")
    {
        var started = rig.Service.Start(SocialPlatforms.Facebook, user, Redirect);
        Assert.True(started.IsSuccess, started.Error);
        return rig.Connector.LastState!;
    }

    [Fact]
    public async Task A_finished_connection_stores_each_account_with_its_token_encrypted_audits_it_and_subscribes_it()
    {
        var rig = Build();
        var state = StartedState(rig);

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-1", "CODE1", state, Redirect, Webhook, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var account = Assert.Single(result.Value!.Accounts);
        Assert.True(Assert.Single(result.Value.Subscriptions).Active);

        var stored = Assert.Single(rig.Connected);
        Assert.Equal("PAGE1", stored.PlatformAccountId);
        Assert.Equal(Now.UtcDateTime, stored.ConnectedAt);
        Assert.NotEmpty(stored.Token.Cipher);
        Assert.DoesNotContain("PRIVATE-PAGE-TOKEN", System.Text.Encoding.UTF8.GetString(stored.Token.Cipher), StringComparison.Ordinal);

        Assert.Equal(new[] { ActionKinds.Connect, ActionKinds.SubscribeWebhook }, rig.Store.Audits.Select(a => a.Action));
        Assert.All(rig.Store.Audits, a => Assert.Equal("user-1", a.ActorUserId));
        Assert.Contains(("accountChanged", account.Id, account.Id, SocialAccountStatuses.Connected), rig.Realtime.Sent);
        Assert.DoesNotContain(rig.Log.Messages, m => m.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_state_started_by_another_user_is_refused_before_any_exchange()
    {
        var rig = Build();
        var state = StartedState(rig, "user-1");

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-2", "CODE1", state, Redirect, Webhook, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Forbidden, result.ErrorCode);
        Assert.Equal(0, rig.Connector.Exchanges);
    }

    [Fact]
    public async Task A_state_for_another_return_address_is_refused()
    {
        var rig = Build();
        var state = StartedState(rig);

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-1", "CODE1", state,
            new Uri("https://elsewhere.example.com/callback"), Webhook, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Forbidden, result.ErrorCode);
        Assert.Equal(0, rig.Connector.Exchanges);
    }

    [Fact]
    public async Task An_altered_or_expired_state_is_refused_and_logged()
    {
        var rig = Build();

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-1", "CODE1", "not-a-state", Redirect, Webhook, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(0, rig.Connector.Exchanges);
        Assert.Equal(LogLevel.Warning, Assert.Single(rig.Log.Entries).Level);
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_state_past_its_lifetime_is_refused_before_any_exchange()
    {
        var clock = new MovableClock(Now);
        var rig = Build(clock);
        var state = StartedState(rig);
        clock.Now = Now + SocialConnectService.StateLifetime + TimeSpan.FromSeconds(1);

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-1", "CODE1", state, Redirect, Webhook, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(0, rig.Connector.Exchanges);
    }

    [Fact]
    public async Task A_subscription_that_failed_is_shown_with_its_account_audited_and_logged()
    {
        var rig = Build();
        rig.Connector.OnSubscribe = () => Result<SocialWebhookSubscription>.Failure("Meta did not subscribe the Page.", SocialFailureCodes.Forbidden);
        var state = StartedState(rig);

        var result = await rig.Service.CompleteAsync(SocialPlatforms.Facebook, "user-1", "CODE1", state, Redirect, Webhook, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var subscription = Assert.Single(result.Value!.Subscriptions);
        Assert.False(subscription.Active);
        Assert.Equal(SocialFailureCodes.Forbidden, subscription.Code);
        Assert.Equal(ActionOutcomes.Failed, rig.Store.Audits[^1].Outcome);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void A_platform_with_no_connector_cannot_be_started()
    {
        var rig = Build();

        var started = rig.Service.Start(SocialPlatforms.LinkedInOrganization, "user-1", Redirect);

        Assert.Equal(SocialFailureCodes.NotSupported, started.ErrorCode);
    }
}
