using System.Security.Cryptography;
using GeekAPI.Services.Social;
using GeekAPI.Services.Social.Connector;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The token provider decides, for each request, whether a stored token is good, needs renewing, or
/// means the account must be reconnected. Every branch of that decision has a test, and none of them
/// may leave a token in a message.
/// </summary>
public class SocialTokenProviderTests
{
    private const string OldTokenValue = "EAAB-old-token-not-real";
    private const string NewTokenValue = "EAAB-new-token-not-real";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed class FakeConnector(Func<SocialToken, Result<SocialToken>>? refresh) : ISocialAccountConnector
    {
        public int Refreshes { get; private set; }
        public string Platform => SocialPlatforms.Facebook;

        public Task<Result<SocialToken>> RefreshTokenAsync(SocialToken token, CancellationToken ct = default)
        {
            Refreshes++;
            return Task.FromResult((refresh ?? throw new InvalidOperationException("Renewal was not expected."))(token));
        }

        public Result<Uri> BuildAuthorizationUrl(string state, Uri redirectUri) => throw new NotSupportedException();
        public Task<Result<SocialConnection>> ExchangeAuthorizationCodeAsync(string code, Uri redirectUri, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<SocialTokenStatus>> GetTokenStatusAsync(SocialToken token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<SocialWebhookSubscription>> EnsureWebhookSubscriptionAsync(SocialAccount account, SocialToken token, Uri callbackUrl, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public SocialTokenProvider Provider { get; set; } = null!;
        public required FakeSocialStore Store { get; init; }
        public required SocialCredentialProtector Protector { get; init; }
        public FakeConnector? Connector { get; init; }
        public List<StoreSocialTokenCommand> Stored { get; } = new();
        public List<Guid> MarkedReconnect { get; } = new();
        public List<ActionAudit> Audits { get; } = new();
    }

    private static Harness Build(
        DateTime? expiresAt,
        string status = SocialAccountStatuses.Connected,
        FakeConnector? connector = null,
        Func<string?>? key = null)
    {
        var protector = new SocialCredentialProtector(key ?? (() => Key));
        var sealedToken = new SocialCredentialProtector(() => Key).Protect(new SocialToken(OldTokenValue, expiresAt)).Value!;
        var store = new FakeSocialStore();
        var harness = new Harness { Store = store, Protector = protector, Connector = connector };

        store.OnGetAccount = id => Result<SocialAccount>.Success(
            new SocialAccount { Id = id, Platform = SocialPlatforms.Facebook, Status = status });
        store.OnGetCredential = id => Result<SocialAccountCredential>.Success(
            new SocialAccountCredential { AccountId = id, Token = sealedToken });
        store.OnStoreToken = (id, command) =>
        {
            harness.Stored.Add(command);
            return Result<SocialAccount>.Success(new SocialAccount { Id = id, Platform = SocialPlatforms.Facebook });
        };
        store.OnMarkReconnectRequired = id =>
        {
            harness.MarkedReconnect.Add(id);
            return Result<SocialAccount>.Success(new SocialAccount { Id = id, Status = SocialAccountStatuses.ReconnectRequired });
        };
        store.OnRecordAudit = audit =>
        {
            harness.Audits.Add(audit);
            return Result<ActionAudit>.Success(audit);
        };

        var services = new ServiceCollection();
        if (connector is not null)
            services.AddKeyedSingleton<ISocialAccountConnector>(SocialPlatforms.Facebook, (_, _) => connector);

        harness.Provider = new SocialTokenProvider(
            store, protector, services.BuildServiceProvider(),
            new MetaEngagementReaderTests.FixedClock(Now), NullLogger<SocialTokenProvider>.Instance);
        return harness;
    }

    private static Guid AccountId => Guid.NewGuid();

    private static DateTime In(TimeSpan span) => Now.UtcDateTime + span;

    [Fact]
    public async Task A_token_with_no_expiry_is_returned_as_stored_and_never_renewed()
    {
        var h = Build(expiresAt: null, connector: new FakeConnector(null));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.True(token.IsSuccess, token.Error);
        Assert.Equal(OldTokenValue, token.Value!.Reveal());
        Assert.Equal(0, h.Connector!.Refreshes);
        Assert.Empty(h.Stored);
        Assert.Empty(h.Audits);
    }

    [Fact]
    public async Task A_token_outside_the_renewal_window_is_returned_unchanged()
    {
        var h = Build(In(SocialTokenProvider.RenewalWindow + TimeSpan.FromHours(1)), connector: new FakeConnector(null));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(OldTokenValue, token.Value!.Reveal());
        Assert.Equal(0, h.Connector!.Refreshes);
    }

    [Fact]
    public async Task A_token_inside_the_window_is_renewed_stored_encrypted_audited_and_returned()
    {
        var newExpiry = In(TimeSpan.FromDays(60));
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Success(new SocialToken(NewTokenValue, newExpiry))));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.True(token.IsSuccess, token.Error);
        Assert.Equal(NewTokenValue, token.Value!.Reveal());
        Assert.Equal(newExpiry, token.Value.ExpiresAt);
        var stored = Assert.Single(h.Stored).Token;
        Assert.Equal(newExpiry, stored.ExpiresAt);
        Assert.DoesNotContain(NewTokenValue, System.Text.Encoding.UTF8.GetString(stored.Cipher));
        Assert.Equal(NewTokenValue, h.Protector.Unprotect(stored).Value!.Reveal());
        var audit = Assert.Single(h.Audits);
        Assert.Equal((ActionKinds.RefreshToken, ActionOutcomes.Succeeded, null), (audit.Action, audit.Outcome, audit.FailureCode));
        Assert.Equal(SocialPlatforms.Facebook, audit.Platform);
        Assert.Empty(h.MarkedReconnect);
    }

    [Fact]
    public async Task A_platform_with_no_renewal_leaves_a_token_that_is_still_good_in_use()
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Failure("none", SocialFailureCodes.NotSupported)));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(OldTokenValue, token.Value!.Reveal());
        Assert.Empty(h.Stored);
        Assert.Empty(h.Audits);
        Assert.Empty(h.MarkedReconnect);
    }

    [Theory]
    [InlineData(SocialFailureCodes.TokenInvalid)]
    [InlineData(SocialFailureCodes.Forbidden)]
    public async Task A_renewal_the_platform_refuses_marks_the_account_reconnect_required_and_audits_it(string code)
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Failure("refused", code)));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(code, token.ErrorCode);
        Assert.Single(h.MarkedReconnect);
        var audit = Assert.Single(h.Audits);
        Assert.Equal((ActionOutcomes.Failed, code), (audit.Outcome, audit.FailureCode));
        Assert.Empty(h.Stored);
    }

    [Theory]
    [InlineData(SocialFailureCodes.PlatformUnavailable)]
    [InlineData(SocialFailureCodes.RateLimited)]
    [InlineData(SocialFailureCodes.PlatformRejected)]
    public async Task A_renewal_that_failed_for_another_reason_fails_the_request_without_marking_the_account(string code)
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Failure("later", code)));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(code, token.ErrorCode);
        Assert.Empty(h.MarkedReconnect);
        Assert.Equal(ActionOutcomes.Failed, Assert.Single(h.Audits).Outcome);
    }

    [Fact]
    public async Task An_expired_token_marks_the_account_and_fails_without_asking_the_platform()
    {
        var h = Build(In(TimeSpan.FromMinutes(-1)), connector: new FakeConnector(null));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.TokenInvalid, token.ErrorCode);
        Assert.Single(h.MarkedReconnect);
        Assert.Equal(0, h.Connector!.Refreshes);
    }

    [Fact]
    public async Task An_account_already_marked_reconnect_required_gets_no_token_and_its_credential_is_not_read()
    {
        var h = Build(null, SocialAccountStatuses.ReconnectRequired);
        h.Store.OnGetCredential = _ => throw new InvalidOperationException("The credential must not be read.");

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.TokenInvalid, token.ErrorCode);
        Assert.Empty(h.MarkedReconnect);
    }

    [Fact]
    public async Task A_store_that_cannot_answer_fails_the_request_with_the_store_code()
    {
        var noAccount = Build(null);
        noAccount.Store.OnGetAccount = _ => Result<SocialAccount>.Failure("down", SocialFailureCodes.StoreUnavailable);
        var noCredential = Build(null);
        noCredential.Store.OnGetCredential = _ => Result<SocialAccountCredential>.Failure("gone", SocialFailureCodes.NotFound);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, (await noAccount.Provider.GetTokenAsync(AccountId)).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await noCredential.Provider.GetTokenAsync(AccountId)).ErrorCode);
    }

    [Fact]
    public async Task A_missing_or_wrong_key_is_the_operators_problem_and_never_marks_the_account()
    {
        var noKey = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(null), key: () => null);
        var wrongKey = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(null), key: () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        foreach (var h in new[] { noKey, wrongKey })
        {
            var token = await h.Provider.GetTokenAsync(AccountId);

            Assert.Equal(SocialFailureCodes.EncryptionUnavailable, token.ErrorCode);
            Assert.Empty(h.MarkedReconnect);
            Assert.Equal(0, h.Connector!.Refreshes);
        }
    }

    [Fact]
    public async Task When_the_store_will_not_record_reconnect_required_the_caller_sees_the_store_failure()
    {
        var h = Build(In(TimeSpan.FromMinutes(-1)));
        h.Store.OnMarkReconnectRequired = _ => Result<SocialAccount>.Failure("down", SocialFailureCodes.StoreUnavailable);

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, token.ErrorCode);
    }

    [Fact]
    public async Task A_token_in_the_window_with_no_connector_registered_is_not_configured_and_not_silently_used()
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: null);

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.NotConfigured, token.ErrorCode);
    }

    [Fact]
    public async Task A_renewed_token_that_cannot_be_stored_is_not_handed_out_as_if_it_were_kept()
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Success(new SocialToken(NewTokenValue, In(TimeSpan.FromDays(60))))));
        h.Store.OnStoreToken = (_, _) => Result<SocialAccount>.Failure("down", SocialFailureCodes.StoreUnavailable);

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, token.ErrorCode);
        Assert.Empty(h.Audits);
    }

    [Fact]
    public async Task When_the_audit_of_a_renewal_cannot_be_written_the_request_fails_loudly()
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(_ => Result<SocialToken>.Success(new SocialToken(NewTokenValue, In(TimeSpan.FromDays(60))))));
        h.Store.OnRecordAudit = _ => Result<ActionAudit>.Failure("down", SocialFailureCodes.StoreUnavailable);

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, token.ErrorCode);
    }

    [Fact]
    public async Task No_failure_message_carries_a_token()
    {
        var h = Build(In(TimeSpan.FromDays(2)), connector: new FakeConnector(t => Result<SocialToken>.Failure("Meta refused the call (HTTP 400, error 190).", SocialFailureCodes.TokenInvalid)));

        var token = await h.Provider.GetTokenAsync(AccountId);

        Assert.DoesNotContain(OldTokenValue, token.Error);
        Assert.DoesNotContain("EAAB", token.Error);
        Assert.All(h.Audits, a => Assert.DoesNotContain("EAAB", a.FailureDetail ?? ""));
    }

    [Fact]
    public void The_provider_is_registered_for_the_contract_the_orchestrator_will_ask_for()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("GeekRepository");
        services.AddSocialPersistence();
        services.AddSingleton(TimeProvider.System);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.IsType<SocialTokenProvider>(scope.ServiceProvider.GetRequiredService<ISocialTokenProvider>());
    }
}
