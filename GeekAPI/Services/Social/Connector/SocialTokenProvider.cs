using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Connector;

/// <summary>
/// Hands out a usable token for an account, and is the only component that decrypts a stored one.
/// A token inside its renewal window is renewed by the platform's connector when it is asked for,
/// and an account whose platform refuses the token is marked reconnect_required. There is no timer
/// (AGENTS.md forbids polling): a token nobody asks for is never renewed, and one that expires
/// meanwhile is found out the next time it is asked for.
/// </summary>
/// <remarks>
/// What each outcome does:
/// <list type="bullet">
/// <item>No expiry on the token (a Facebook Page token): returned as stored, never renewed.</item>
/// <item>Past its expiry: the account is marked reconnect_required and the request fails token_invalid.</item>
/// <item>Inside the window and the platform renews it: the new token is stored first, then returned.</item>
/// <item>Inside the window and the platform has no renewal (not_supported): the token is still good, so it is returned as is.</item>
/// <item>Inside the window and the platform refuses it (token_invalid or forbidden): reconnect_required, and the request fails with that code.</item>
/// <item>Inside the window and the platform is down or throttling: the request fails with that failure. The account is not marked, because nothing says the grant is gone, and the next request tries again.</item>
/// </list>
/// A key problem (encryption_unavailable) is the operator's to fix and never marks an account.
/// </remarks>
public sealed class SocialTokenProvider : ISocialTokenProvider
{
    /// <summary>How long before its expiry a token starts being renewed when asked for.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(7);

    private readonly ISocialStore _store;
    private readonly SocialCredentialProtector _protector;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialTokenProvider> _logger;

    public SocialTokenProvider(
        ISocialStore store,
        SocialCredentialProtector protector,
        IServiceProvider services,
        TimeProvider clock,
        ILogger<SocialTokenProvider> logger)
    {
        _store = store;
        _protector = protector;
        _services = services;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<SocialToken>> GetTokenAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
            return Result<SocialToken>.Failure(account.Error!, account.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        if (account.Value!.Status == SocialAccountStatuses.ReconnectRequired)
            return Result<SocialToken>.Failure(
                "The account must be reconnected before anything can be sent from it.", SocialFailureCodes.TokenInvalid);

        var credential = await _store.GetCredentialAsync(accountId, ct).ConfigureAwait(false);
        if (!credential.IsSuccess)
            return Result<SocialToken>.Failure(credential.Error!, credential.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        var opened = _protector.Unprotect(credential.Value!.Token);
        if (!opened.IsSuccess)
            return opened;

        var token = opened.Value!;
        var now = _clock.GetUtcNow().UtcDateTime;
        if (token.ExpiresAt is not { } expiresAt)
            return Result<SocialToken>.Success(token);

        if (expiresAt <= now)
            return await RequireReconnectAsync(account.Value, "The token has expired.", SocialFailureCodes.TokenInvalid, ct).ConfigureAwait(false);

        if (expiresAt - now > RenewalWindow)
            return Result<SocialToken>.Success(token);

        return await RenewAsync(account.Value, token, now, ct).ConfigureAwait(false);
    }

    private async Task<Result<SocialToken>> RenewAsync(SocialAccount account, SocialToken token, DateTime now, CancellationToken ct)
    {
        var connector = _services.GetKeyedService<ISocialAccountConnector>(account.Platform);
        if (connector is null)
            return Result<SocialToken>.Failure(
                $"No connector is registered for {account.Platform}, so its token cannot be renewed.", SocialFailureCodes.NotConfigured);

        var renewed = await connector.RefreshTokenAsync(token, ct).ConfigureAwait(false);
        if (!renewed.IsSuccess)
        {
            switch (renewed.ErrorCode)
            {
                case SocialFailureCodes.NotSupported:
                    return Result<SocialToken>.Success(token);

                case SocialFailureCodes.TokenInvalid:
                case SocialFailureCodes.Forbidden:
                    // Marking the account comes first, because that is what stops a send; the audit follows.
                    var reconnect = await RequireReconnectAsync(account, renewed.Error!, renewed.ErrorCode!, ct).ConfigureAwait(false);
                    var refusedAudit = await AuditAsync(account, ActionOutcomes.Failed, renewed.ErrorCode, now, ct).ConfigureAwait(false);
                    return refusedAudit.IsSuccess
                        ? reconnect
                        : Result<SocialToken>.Failure(refusedAudit.Error!, refusedAudit.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

                default:
                    var audited = await AuditAsync(account, ActionOutcomes.Failed, renewed.ErrorCode, now, ct).ConfigureAwait(false);
                    return audited.IsSuccess
                        ? Result<SocialToken>.Failure(renewed.Error!, renewed.ErrorCode ?? SocialFailureCodes.PlatformUnavailable)
                        : Result<SocialToken>.Failure(audited.Error!, audited.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
            }
        }

        var sealedToken = _protector.Protect(renewed.Value!);
        if (!sealedToken.IsSuccess)
            return Result<SocialToken>.Failure(sealedToken.Error!, sealedToken.ErrorCode!);

        var stored = await _store.StoreTokenAsync(account.Id, new StoreSocialTokenCommand { Token = sealedToken.Value! }, ct).ConfigureAwait(false);
        if (!stored.IsSuccess)
            return Result<SocialToken>.Failure(stored.Error!, stored.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        var audit = await AuditAsync(account, ActionOutcomes.Succeeded, null, now, ct).ConfigureAwait(false);
        return audit.IsSuccess
            ? Result<SocialToken>.Success(renewed.Value!)
            : Result<SocialToken>.Failure(audit.Error!, audit.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
    }

    private async Task<Result<SocialToken>> RequireReconnectAsync(SocialAccount account, string reason, string code, CancellationToken ct)
    {
        var marked = await _store.MarkReconnectRequiredAsync(account.Id, ct).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            // The grant is gone and the store would not say so: the caller must see the store's failure, not a quiet token_invalid.
            _logger.LogError("Account {AccountId} could not be marked reconnect_required: {Code}.", account.Id, marked.ErrorCode);
            return Result<SocialToken>.Failure(marked.Error!, marked.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
        }

        _logger.LogWarning("Account {AccountId} ({Platform}) now needs reconnecting: {Code}.", account.Id, account.Platform, code);
        return Result<SocialToken>.Failure(reason, code);
    }

    private Task<Result<ActionAudit>> AuditAsync(SocialAccount account, string outcome, string? failureCode, DateTime at, CancellationToken ct) =>
        _store.RecordAuditAsync(new ActionAudit
        {
            AccountId = account.Id,
            Platform = account.Platform,
            Action = ActionKinds.RefreshToken,
            Outcome = outcome,
            FailureCode = outcome == ActionOutcomes.Succeeded ? null : failureCode ?? SocialFailureCodes.PlatformRejected,
            FailureDetail = outcome == ActionOutcomes.Succeeded ? null : "The platform did not renew the token.",
            OccurredAt = at,
        }, ct);
}
