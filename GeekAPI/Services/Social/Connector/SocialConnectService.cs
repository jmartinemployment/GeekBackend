using System.Security.Cryptography;
using GeekAPI.Services.Social.Realtime;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.DataProtection;

namespace GeekAPI.Services.Social.Connector;

/// <summary>What one finished connection stored, Page by Page.</summary>
public sealed class SocialConnectResult
{
    public List<SocialAccount> Accounts { get; set; } = new();
    public List<SocialConnectSubscription> Subscriptions { get; set; } = new();
}

/// <summary>Whether a connected Page's webhook subscription is in place. A failed one carries its reason.</summary>
public sealed class SocialConnectSubscription
{
    public Guid AccountId { get; set; }
    public bool Active { get; set; }
    public string? Code { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// The connect flow a signed-in operator runs from the screen: start (the platform's consent address,
/// with a state only this user can finish within 15 minutes), then complete (trade the code for the
/// accounts, encrypt and store each token, subscribe each account's webhook). Each account stored and
/// each subscription is an audit row. A token never leaves this class except as ciphertext to the store.
/// </summary>
public sealed class SocialConnectService
{
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private const string Purpose = "GeekSocial.Connect.State.v1";

    private readonly IServiceProvider _services;
    private readonly ISocialStore _store;
    private readonly SocialCredentialProtector _protector;
    private readonly IDataProtector _state;
    private readonly ISocialRealtime _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialConnectService> _logger;

    public SocialConnectService(
        IServiceProvider services,
        ISocialStore store,
        SocialCredentialProtector protector,
        IDataProtectionProvider dataProtection,
        ISocialRealtime realtime,
        TimeProvider clock,
        ILogger<SocialConnectService> logger)
    {
        _services = services;
        _store = store;
        _protector = protector;
        _state = dataProtection.CreateProtector(Purpose);
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The platform's consent address for <paramref name="userId"/>, returning to <paramref name="redirectUri"/>.</summary>
    public Result<Uri> Start(string platform, string userId, Uri redirectUri)
    {
        var connector = ConnectorFor(platform);
        if (connector is null)
            return Result<Uri>.Failure($"GeekSocial cannot connect a {platform} account yet.", SocialFailureCodes.NotSupported);
        if (string.IsNullOrWhiteSpace(userId))
            return Result<Uri>.Failure("A signed-in user is required.", SocialFailureCodes.Forbidden);

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var issued = _clock.GetUtcNow().UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var state = _state.Protect($"{platform}\n{userId}\n{redirectUri}\n{issued}\n{nonce}");
        var url = connector.BuildAuthorizationUrl(state, redirectUri);
        if (!url.IsSuccess)
            _logger.LogError("GeekSocial could not start a {Platform} connection: {Code}. {Error}", platform, url.ErrorCode, url.Error);
        return url;
    }

    /// <summary>
    /// Finishes a connection. The state must be one <see cref="Start"/> issued for this user, platform and
    /// redirect address, within its lifetime. Returns every account stored and whether its webhook is in
    /// place; an account whose subscription failed is still connected and the failure is shown with it.
    /// </summary>
    public async Task<Result<SocialConnectResult>> CompleteAsync(
        string platform, string userId, string code, string state, Uri redirectUri, Uri webhookUrl, CancellationToken ct)
    {
        var connector = ConnectorFor(platform);
        if (connector is null)
            return Result<SocialConnectResult>.Failure($"GeekSocial cannot connect a {platform} account yet.", SocialFailureCodes.NotSupported);

        string payload;
        try
        {
            payload = _state.Unprotect(state);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("A {Platform} connection was refused: its state is expired, altered or from before a restart.", platform);
            return Result<SocialConnectResult>.Failure(
                "This connection link has expired or is not valid. Start the connection again.", SocialFailureCodes.InvalidRequest);
        }

        var parts = payload.Split('\n');
        var now = _clock.GetUtcNow().UtcDateTime;
        if (parts.Length != 5
            || !long.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var issuedTicks)
            || now - new DateTime(issuedTicks, DateTimeKind.Utc) > StateLifetime
            || new DateTime(issuedTicks, DateTimeKind.Utc) > now + TimeSpan.FromMinutes(1))
        {
            _logger.LogWarning("A {Platform} connection was refused: its state is older than {Minutes} minutes.", platform, StateLifetime.TotalMinutes);
            return Result<SocialConnectResult>.Failure(
                "This connection link has expired. Start the connection again.", SocialFailureCodes.InvalidRequest);
        }

        if (parts[0] != platform || parts[1] != userId || parts[2] != redirectUri.ToString())
        {
            _logger.LogWarning("A {Platform} connection was refused: its state was issued for another user, platform or address.", platform);
            return Result<SocialConnectResult>.Failure(
                "This connection was started by another user or for another address. Start it again.", SocialFailureCodes.Forbidden);
        }

        var exchanged = await connector.ExchangeAuthorizationCodeAsync(code, redirectUri, ct).ConfigureAwait(false);
        if (!exchanged.IsSuccess)
        {
            _logger.LogError("A {Platform} connection failed at the code exchange: {Code}. {Error}", platform, exchanged.ErrorCode, exchanged.Error);
            await AuditAsync(null, platform, ActionKinds.Connect, exchanged.ErrorCode, exchanged.Error, null, userId, now, ct).ConfigureAwait(false);
            return Result<SocialConnectResult>.Failure(exchanged.Error!, exchanged.ErrorCode!);
        }

        var result = new SocialConnectResult();
        foreach (var granted in exchanged.Value!.Accounts)
        {
            var sealedToken = _protector.Protect(granted.Token);
            if (!sealedToken.IsSuccess)
            {
                _logger.LogError("A {Platform} token could not be encrypted: {Code}.", platform, sealedToken.ErrorCode);
                return Result<SocialConnectResult>.Failure(sealedToken.Error!, sealedToken.ErrorCode!);
            }

            var stored = await _store.ConnectAccountAsync(new ConnectSocialAccountCommand
            {
                Platform = granted.Platform,
                PlatformAccountId = granted.PlatformAccountId,
                DisplayName = granted.DisplayName,
                Token = sealedToken.Value!,
                ConnectedAt = now,
            }, ct).ConfigureAwait(false);
            if (!stored.IsSuccess)
            {
                _logger.LogError("A connected {Platform} account could not be stored: {Code}. {Error}", platform, stored.ErrorCode, stored.Error);
                return Result<SocialConnectResult>.Failure(
                    $"{stored.Error} {result.Accounts.Count} account(s) were stored before it.", stored.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
            }

            var account = stored.Value!;
            result.Accounts.Add(account);
            await AuditAsync(account.Id, platform, ActionKinds.Connect, null, null, account.PlatformAccountId, userId, now, ct).ConfigureAwait(false);
            await _realtime.AccountChangedAsync(account.Id, account.Status, ct).ConfigureAwait(false);

            var subscribed = await connector.EnsureWebhookSubscriptionAsync(account, granted.Token, webhookUrl, ct).ConfigureAwait(false);
            await AuditAsync(account.Id, platform, ActionKinds.SubscribeWebhook,
                subscribed.IsSuccess ? null : subscribed.ErrorCode, subscribed.Error, account.PlatformAccountId, userId, now, ct).ConfigureAwait(false);
            if (!subscribed.IsSuccess)
            {
                _logger.LogError("Account {AccountId} is connected and its webhook subscription failed: {Code}. {Error}",
                    account.Id, subscribed.ErrorCode, subscribed.Error);
            }

            result.Subscriptions.Add(new SocialConnectSubscription
            {
                AccountId = account.Id,
                Active = subscribed.IsSuccess && subscribed.Value!.Active,
                Code = subscribed.IsSuccess ? null : subscribed.ErrorCode,
                Error = subscribed.IsSuccess ? null : subscribed.Error,
            });
        }

        _logger.LogInformation("GeekSocial connected {Count} {Platform} account(s).", result.Accounts.Count, platform);
        return Result<SocialConnectResult>.Success(result);
    }

    private ISocialAccountConnector? ConnectorFor(string platform) =>
        SocialPlatforms.IsKnown(platform) ? _services.GetKeyedService<ISocialAccountConnector>(platform) : null;

    private async Task AuditAsync(
        Guid? accountId, string platform, string action, string? failureCode, string? error,
        string? platformObjectId, string userId, DateTime at, CancellationToken ct)
    {
        var succeeded = failureCode is null && error is null;
        var stored = await _store.RecordAuditAsync(new ActionAudit
        {
            AccountId = accountId,
            Platform = platform,
            Action = action,
            Outcome = succeeded ? ActionOutcomes.Succeeded : ActionOutcomes.Failed,
            PlatformObjectId = platformObjectId,
            FailureCode = succeeded ? null : SocialFailureCodes.IsKnown(failureCode) ? failureCode : SocialFailureCodes.PlatformRejected,
            FailureDetail = succeeded ? null : error is { Length: > 500 } ? error[..500] : error,
            ActorUserId = userId,
            OccurredAt = at,
        }, ct).ConfigureAwait(false);

        if (!stored.IsSuccess)
            _logger.LogError("The {Action} audit row for {Platform} could not be stored: {Code}.", action, platform, stored.ErrorCode);
    }
}
