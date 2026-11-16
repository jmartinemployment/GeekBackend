using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>
/// The owner paying an overage the automatic charge could not take (decided 2026-11-16): the portal asks for a PayPal order for one
/// unpaid charge, the owner approves it in PayPal, and PayPal sends them back to the portal, which asks for it to be completed. Only a
/// charge still unpaid can be paid, and only its exact amount is accepted. A payment is recorded on the charge, and a tenant that is
/// then clear is restored at once, its number answering again, without waiting for the hourly sweep.
/// </summary>
public sealed class OverageCollection
{
    private readonly IVoiceBillingStore _store;
    private readonly PayPalCycleBilling _paypal;
    private readonly TenantStanding _standing;
    private readonly ILogger<OverageCollection> _logger;

    public OverageCollection(IVoiceBillingStore store, PayPalCycleBilling paypal, TenantStanding standing, ILogger<OverageCollection> logger)
    {
        _store = store;
        _paypal = paypal;
        _standing = standing;
        _logger = logger;
    }

    public Task<Result<VoiceBillingStanding>> GetStandingAsync(Guid tenantId, CancellationToken ct = default) => _store.GetStandingAsync(tenantId, ct);

    public async Task<Result<OwnerOverageOrder>> StartAsync(Guid tenantId, Guid chargeId, Uri returnUrl, Uri cancelUrl, CancellationToken ct = default)
    {
        var charge = await UnpaidAsync(tenantId, chargeId, ct).ConfigureAwait(false);
        if (!charge.IsSuccess) return Result<OwnerOverageOrder>.Failure(charge.Error!, charge.ErrorCode!);
        return await _paypal.CreateOwnerOrderAsync(charge.Value!, returnUrl, cancelUrl, ct).ConfigureAwait(false);
    }

    public async Task<Result<VoiceBillingStanding>> CompleteAsync(Guid tenantId, Guid chargeId, string orderId, CancellationToken ct = default)
    {
        var charge = await UnpaidAsync(tenantId, chargeId, ct).ConfigureAwait(false);
        if (!charge.IsSuccess) return Result<VoiceBillingStanding>.Failure(charge.Error!, charge.ErrorCode!);

        var paid = await _paypal.CaptureOwnerOrderAsync(charge.Value!, orderId, ct).ConfigureAwait(false);
        if (!paid.IsSuccess) return Result<VoiceBillingStanding>.Failure(paid.Error!, paid.ErrorCode!);

        var collected = await _store.CollectOverageChargeAsync(tenantId, chargeId,
            new CollectVoiceOverageChargeCommand(paid.Value!.PaypalOrderId!, paid.Value.PaypalCaptureId!), ct).ConfigureAwait(false);
        if (!collected.IsSuccess)
        {
            // PayPal has the money and the store does not know it: this line is the only record until it is fixed by hand.
            _logger.LogCritical("Tenant {TenantId} paid overage charge {ChargeId} (PayPal order {OrderId}, capture {CaptureId}) and the payment could not be recorded: {Code}. {Error}",
                tenantId, chargeId, paid.Value.PaypalOrderId, paid.Value.PaypalCaptureId, collected.ErrorCode, collected.Error);
            return Result<VoiceBillingStanding>.Failure("The payment went through and could not be recorded; it has been logged for us to fix.", collected.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        _logger.LogInformation("Tenant {TenantId} paid overage charge {ChargeId}: {Amount} USD (PayPal order {OrderId}).", tenantId, chargeId, charge.Value!.AmountUsd, paid.Value.PaypalOrderId);

        var standing = await _store.GetStandingAsync(tenantId, ct).ConfigureAwait(false);
        if (!standing.IsSuccess) return standing;
        if (standing.Value!.TenantStatus != VoiceTenantStatuses.Suspended || !TenantStanding.IsClear(standing.Value)) return standing;

        // The payment is recorded either way; a restore that fails here is logged, and the hourly sweep restores the tenant.
        var restored = await _standing.RestoreAsync(standing.Value, ct).ConfigureAwait(false);
        return restored.IsSuccess ? restored : standing;
    }

    private async Task<Result<VoiceOverageCharge>> UnpaidAsync(Guid tenantId, Guid chargeId, CancellationToken ct)
    {
        var standing = await _store.GetStandingAsync(tenantId, ct).ConfigureAwait(false);
        if (!standing.IsSuccess) return Result<VoiceOverageCharge>.Failure(standing.Error!, standing.ErrorCode!);

        var charge = standing.Value!.UnpaidCharges.FirstOrDefault(c => c.Id == chargeId);
        return charge is null
            ? Result<VoiceOverageCharge>.Failure("That overage is not owed: it is paid, or it is not yours.", BillingFailureCodes.ChargeNotOwed)
            : Result<VoiceOverageCharge>.Success(charge);
    }
}
