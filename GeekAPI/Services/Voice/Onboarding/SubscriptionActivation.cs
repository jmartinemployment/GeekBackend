using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>Why an activation was refused beyond the codes PayPal, Twilio and the store already carry.</summary>
public static class ActivationFailureCodes
{
    /// <summary>The signup asked for SMS notifications, and A2P 10DLC brand registration is not built, so nothing is activated.</summary>
    public const string SmsNotificationsNotAvailable = "sms_notifications_not_available";

    /// <summary>The tenant is not waiting for payment: it is already active, suspended or cancelled.</summary>
    public const string TenantNotPending = "tenant_not_pending";
}

/// <summary>
/// What the third step of /signup sends (docs/Geek-Receptionist.md section 5, item 2). Every field is nullable so a field that was
/// left out is told apart from one that was sent empty.
/// </summary>
public sealed record ActivateSubscriptionRequest(
    Guid? TenantId,
    string? TierCode,
    string? PaypalSubscriptionId,
    string? PaypalVaultTokenId,
    string? PreferredAreaCode,
    bool? EnableSmsNotifications);

/// <summary>The tenant is active: its tier and the number that now answers for it.</summary>
public sealed record SubscriptionActivated(Guid TenantId, string Status, string TierCode, int IncludedMinutes, string AssignedPhoneNumber);

/// <summary>
/// Activates a tenant that is waiting for payment (docs/Geek-Receptionist.md section 5, items 2 to 4). Everything that can be refused
/// without creating anything is refused first: the request, the tenant's state, every variable the later steps read, and the
/// encryption key. PayPal is then asked whether the subscription is paid, and only after that is anything created: the Twilio
/// subaccount and the number on it, whose calls and call statuses Twilio posts to GeekAPI, then the tenant's starting settings and,
/// last, the tenant's activation (one transaction in GeekRepository). A failure after something exists closes the Twilio subaccount
/// once, leaves the tenant waiting for payment and answers with the step that failed. The settings are saved before the activation so a failed
/// activation leaves nothing that blocks a second try. Nothing is retried here, and nothing is activated with a value the signup did
/// not supply.
/// </summary>
public sealed class SubscriptionActivation
{
    /// <summary>A2P status of a tenant that did not ask for SMS notifications.</summary>
    public const string A2pNotRequested = "not_requested";

    private readonly IVoiceStore _store;
    private readonly PayPalBillingVerifier _billing;
    private readonly TwilioProvisioner _twilio;
    private readonly VoiceCredentialProtector _protector;
    private readonly ILogger<SubscriptionActivation> _logger;

    public SubscriptionActivation(
        IVoiceStore store,
        PayPalBillingVerifier billing,
        TwilioProvisioner twilio,
        VoiceCredentialProtector protector,
        ILogger<SubscriptionActivation> logger)
    {
        _store = store;
        _billing = billing;
        _twilio = twilio;
        _protector = protector;
        _logger = logger;
    }

    public async Task<Result<SubscriptionActivated>> ActivateAsync(ActivateSubscriptionRequest? request, CancellationToken ct = default)
    {
        if (request?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return Fail("A tenant is required.", VoiceFailureCodes.InvalidRequest);
        if (request.EnableSmsNotifications is null)
            return Fail("Say whether SMS notifications are wanted.", VoiceFailureCodes.InvalidRequest);
        if (request.EnableSmsNotifications.Value)
            return Fail("SMS notifications are not available yet.", ActivationFailureCodes.SmsNotificationsNotAvailable);

        var tenant = await _store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess) return Fail(tenant.Error!, tenant.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (tenant.Value!.Status != VoiceTenantStatuses.PendingPayment)
            return Fail("The tenant is not waiting for payment.", ActivationFailureCodes.TenantNotPending);

        var script = ReceptionistDefaults.ScriptFor(tenant.Value);
        if (!script.IsSuccess) return Fail(script.Error!, script.ErrorCode!);

        var missing = _twilio.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("Tenant {TenantId} cannot be activated: {Variables} not set.", tenantId, string.Join(", ", missing));
            return Fail("Activation is not configured.", TelephonyFailureCodes.TelephonyNotConfigured);
        }

        var keyCheck = _protector.Protect("key check", VoiceSecretPurposes.KeyCheck);
        if (!keyCheck.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId} cannot be activated: {Variable} is not usable.", tenantId, VoiceCredentialProtector.KeyVariable);
            return Fail(keyCheck.Error!, keyCheck.ErrorCode!);
        }

        var billing = await _billing.VerifyAsync(tenantId, request.TierCode, request.PaypalSubscriptionId, request.PaypalVaultTokenId, ct).ConfigureAwait(false);
        if (!billing.IsSuccess) return Fail(billing.Error!, billing.ErrorCode!);

        var telephony = await _twilio.ProvisionAsync(tenantId, request.PreferredAreaCode, smsEnabled: false, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess) return Fail(telephony.Error!, telephony.ErrorCode!);

        var token = _protector.Protect(telephony.Value!.AuthToken, VoiceSecretPurposes.TwilioAuthToken);
        if (!token.IsSuccess) return await UndoAsync(tenantId, telephony.Value, token.Error!, token.ErrorCode!, ct).ConfigureAwait(false);

        var saved = await _store.SaveConfigAsync(tenantId, ReceptionistDefaults.ConfigFor(script.Value!), ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
            return await UndoAsync(tenantId, telephony.Value, "The receptionist settings could not be saved, and the phone setup was undone.", saved.ErrorCode ?? VoiceFailureCodes.StoreUnavailable, ct).ConfigureAwait(false);

        var verified = billing.Value!;
        var command = new ActivateVoiceTenantCommand(
            TierCode: verified.Tier.Code,
            MonthlyPriceUsd: verified.Tier.MonthlyPriceUsd,
            IncludedMinutes: verified.Tier.IncludedMinutes,
            OverageRateUsd: verified.Tier.OverageRateUsd,
            PaypalSubscriptionId: verified.SubscriptionId,
            PaypalVaultTokenId: verified.VaultTokenId,
            CurrentPeriodStart: verified.CurrentPeriodStart,
            CurrentPeriodEnd: verified.CurrentPeriodEnd,
            BillingStatus: verified.BillingStatus,
            TwilioSubaccountSid: telephony.Value.SubaccountSid,
            TwilioAuthTokenEncrypted: token.Value!,
            AssignedPhoneNumber: telephony.Value.PhoneNumber,
            TwilioPhoneSid: telephony.Value.PhoneSid,
            A2pBrandSid: null,
            A2pCampaignSid: null,
            A2pStatus: A2pNotRequested,
            SmsNotificationsEnabled: false);

        var activated = await _store.ActivateTenantAsync(tenantId, command, ct).ConfigureAwait(false);
        if (!activated.IsSuccess)
            return await UndoAsync(tenantId, telephony.Value, "The tenant could not be activated, and the phone setup was undone.", activated.ErrorCode ?? VoiceFailureCodes.StoreUnavailable, ct).ConfigureAwait(false);

        _logger.LogInformation("Tenant {TenantId} was activated on tier {Tier} with number {PhoneSid}.", tenantId, verified.Tier.Code, telephony.Value.PhoneSid);
        return Result<SubscriptionActivated>.Success(new SubscriptionActivated(tenantId, activated.Value!.Status, verified.Tier.Code, verified.Tier.IncludedMinutes, telephony.Value.PhoneNumber));
    }

    /// <summary>One attempt to close the Twilio subaccount after a step that came later failed. The failure that caused it is what is answered.</summary>
    private async Task<Result<SubscriptionActivated>> UndoAsync(Guid tenantId, ProvisionedTelephony telephony, string error, string errorCode, CancellationToken ct)
    {
        _logger.LogError("Activation of tenant {TenantId} failed with {Code}; undoing the phone setup.", tenantId, errorCode);
        await _twilio.AbandonAsync(telephony.SubaccountSid, tenantId, ct).ConfigureAwait(false);
        return Result<SubscriptionActivated>.Failure(error, errorCode);
    }

    private Result<SubscriptionActivated> Fail(string error, string code)
    {
        _logger.LogWarning("Activation was refused with {Code}.", code);
        return Result<SubscriptionActivated>.Failure(error, code);
    }
}
