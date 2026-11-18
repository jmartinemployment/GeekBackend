using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Twilio.Security;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>Why a post to a tenant's call routes was refused. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class TenantCallFailureCodes
{
    /// <summary>A field Twilio always sends is missing or not of its documented kind.</summary>
    public const string InvalidCall = "invalid_call";
    /// <summary>X-Twilio-Signature is missing or was not made with this tenant's subaccount auth token.</summary>
    public const string SignatureInvalid = "invalid_signature";
    /// <summary>The post is signed but names another Twilio account than this tenant's subaccount.</summary>
    public const string AccountMismatch = "account_mismatch";
}

/// <summary>
/// Authenticates a post to a tenant's call routes (<see cref="TwilioProvisioner.TenantVoicePath"/> and
/// <see cref="TwilioProvisioner.TenantCallStatusPath"/>). Twilio signs a webhook for a number with the auth token of the account that
/// owns the number, which for a tenant is its subaccount; that token is kept encrypted in tenant_telephony and opened here with
/// <see cref="VoiceCredentialProtector"/>. As for the company line (TwilioSignatureFilter), the signed URL is rebuilt from
/// PUBLIC_BASE_URL because Railway terminates TLS at its proxy. The check cannot be turned off. A signed post must also name the
/// tenant's own subaccount as its AccountSid, so one tenant's route can never take another account's call. Nothing is logged of the
/// form: no caller number, no token.
/// </summary>
public sealed class TenantTwilioWebhook
{
    public const string SignatureHeader = "X-Twilio-Signature";

    private readonly IVoiceStore _store;
    private readonly VoiceCredentialProtector _protector;
    private readonly TwilioOptions _options;
    private readonly ILogger<TenantTwilioWebhook> _logger;

    public TenantTwilioWebhook(IVoiceStore store, VoiceCredentialProtector protector, TwilioOptions options, ILogger<TenantTwilioWebhook> logger)
    {
        _store = store;
        _protector = protector;
        _options = options;
        _logger = logger;
    }

    /// <summary>The tenant's telephony when the post is Twilio's, signed for this tenant's subaccount; otherwise a failure with its code.</summary>
    public async Task<Result<VoiceTelephony>> VerifyAsync(Guid tenantId, string pathAndQuery, string? signature, IReadOnlyDictionary<string, string> form, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            _logger.LogError("A Twilio post for tenant {TenantId} was refused: PUBLIC_BASE_URL is not set, so its signature cannot be checked.", tenantId);
            return Result<VoiceTelephony>.Failure("Twilio posts cannot be checked.", TelephonyFailureCodes.TelephonyNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("A post for tenant {TenantId} was refused: it carries no Twilio signature.", tenantId);
            return Result<VoiceTelephony>.Failure("The post is not signed.", TenantCallFailureCodes.SignatureInvalid);
        }

        var telephony = await _store.GetTelephonyAsync(tenantId, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess)
        {
            _logger.LogWarning("A Twilio post for tenant {TenantId} was refused: its telephony could not be read ({Code}).", tenantId, telephony.ErrorCode);
            return Result<VoiceTelephony>.Failure(telephony.Error!, telephony.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        var token = _protector.Unprotect(telephony.Value!.TwilioAuthTokenEncrypted, VoiceSecretPurposes.TwilioAuthToken);
        if (!token.IsSuccess)
        {
            _logger.LogError("A Twilio post for tenant {TenantId} was refused: its subaccount token could not be opened ({Code}).", tenantId, token.ErrorCode);
            return Result<VoiceTelephony>.Failure(token.Error!, token.ErrorCode!);
        }

        var parameters = new Dictionary<string, string>(form, StringComparer.Ordinal);
        if (!new RequestValidator(token.Value!).Validate(_options.PublicBaseUrl + pathAndQuery, parameters, signature))
        {
            _logger.LogWarning("A post for tenant {TenantId} was refused: its Twilio signature does not match.", tenantId);
            return Result<VoiceTelephony>.Failure("The post's signature does not match.", TenantCallFailureCodes.SignatureInvalid);
        }

        if (!form.TryGetValue("AccountSid", out var accountSid) || !string.Equals(accountSid, telephony.Value.TwilioSubaccountSid, StringComparison.Ordinal))
        {
            _logger.LogWarning("A signed post for tenant {TenantId} was refused: it does not name the tenant's subaccount.", tenantId);
            return Result<VoiceTelephony>.Failure("The call does not belong to this tenant's account.", TenantCallFailureCodes.AccountMismatch);
        }

        return telephony;
    }

    /// <summary>
    /// The tenant's telephony when ConversationRelay's WebSocket handshake for this tenant carries an X-Twilio-Signature made with the
    /// subaccount's auth token. Twilio's docs say the handshake is signed and do not say over what; it is checked as a webhook is, over
    /// the full address Twilio requested (<paramref name="wssUrl"/>, with its query) and no parameters, because a handshake has no form.
    /// A handshake names no AccountSid, so the one-time relay ticket is what ties it to a call this tenant's signed consent post accepted.
    /// </summary>
    public async Task<Result<VoiceTelephony>> VerifyHandshakeAsync(Guid tenantId, string wssUrl, string? signature, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("A relay connection for tenant {TenantId} was refused: it carries no Twilio signature.", tenantId);
            return Result<VoiceTelephony>.Failure("The connection is not signed.", TenantCallFailureCodes.SignatureInvalid);
        }

        var telephony = await _store.GetTelephonyAsync(tenantId, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess)
        {
            _logger.LogWarning("A relay connection for tenant {TenantId} was refused: its telephony could not be read ({Code}).", tenantId, telephony.ErrorCode);
            return Result<VoiceTelephony>.Failure(telephony.Error!, telephony.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        var token = _protector.Unprotect(telephony.Value!.TwilioAuthTokenEncrypted, VoiceSecretPurposes.TwilioAuthToken);
        if (!token.IsSuccess)
        {
            _logger.LogError("A relay connection for tenant {TenantId} was refused: its subaccount token could not be opened ({Code}).", tenantId, token.ErrorCode);
            return Result<VoiceTelephony>.Failure(token.Error!, token.ErrorCode!);
        }

        if (!new RequestValidator(token.Value!).Validate(wssUrl, new Dictionary<string, string>(), signature))
        {
            _logger.LogWarning("A relay connection for tenant {TenantId} was refused: its Twilio signature does not match.", tenantId);
            return Result<VoiceTelephony>.Failure("The connection's signature does not match.", TenantCallFailureCodes.SignatureInvalid);
        }

        return telephony;
    }
}
