using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>The portal's request for a PayPal order: where PayPal sends the owner after approving, and after cancelling.</summary>
public sealed record OwnerPayRequest(string? ReturnUrl, string? CancelUrl);

/// <summary>The portal's request to complete the PayPal order the owner approved.</summary>
public sealed record OwnerCompleteRequest(string? OrderId);

/// <summary>
/// The owner's billing (docs/Geek-Receptionist.md section 5, item 2): where their tenant stands, and paying an overage the automatic
/// charge could not take. The tenant comes from the owner's tenant_id claim and a URL naming another tenant is refused (403). Nothing
/// here answers PayPal's subscription id, the saved payment method or a failure's wording; the owner sees their status, the date their
/// number stops answering, and what they owe.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class BillingController(OverageCollection collection) : ControllerBase
{
    [HttpGet("api/v1/tenants/{tenantId:guid}/billing")]
    public async Task<IActionResult> Standing(Guid tenantId, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var standing = await collection.GetStandingAsync(tenantId, ct);
        return standing.IsSuccess ? Ok(View(standing.Value!)) : Failure(standing.Error, standing.ErrorCode);
    }

    [HttpPost("api/v1/tenants/{tenantId:guid}/billing/overage-charges/{chargeId:guid}/pay")]
    public async Task<IActionResult> Pay(Guid tenantId, Guid chargeId, [FromBody] OwnerPayRequest? request, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var returnUrl = ReturnAddress.Parse(request?.ReturnUrl);
        var cancelUrl = ReturnAddress.Parse(request?.CancelUrl);
        if (returnUrl is null || cancelUrl is null)
            return Failure("Both return addresses must be https addresses (http only for localhost).", VoiceFailureCodes.InvalidRequest);

        var order = await collection.StartAsync(tenantId, chargeId, returnUrl, cancelUrl, ct);
        return order.IsSuccess ? Ok(new { orderId = order.Value!.OrderId, approvalUrl = order.Value.ApprovalUrl }) : Failure(order.Error, order.ErrorCode);
    }

    [HttpPost("api/v1/tenants/{tenantId:guid}/billing/overage-charges/{chargeId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid tenantId, Guid chargeId, [FromBody] OwnerCompleteRequest? request, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var orderId = request?.OrderId?.Trim();
        if (string.IsNullOrEmpty(orderId) || orderId.Length > 64 || !orderId.All(char.IsAsciiLetterOrDigit))
            return Failure("A PayPal order id is required.", VoiceFailureCodes.InvalidRequest);

        var standing = await collection.CompleteAsync(tenantId, chargeId, orderId, ct);
        return standing.IsSuccess ? Ok(View(standing.Value!)) : Failure(standing.Error, standing.ErrorCode);
    }

    internal static object View(VoiceBillingStanding s) => new
    {
        status = s.TenantStatus,
        suspendedAt = s.SuspendedAt,
        answeringUntil = s.SuspendedAt + TenantStanding.AnsweringGrace,
        answeringStopped = s.AnsweringStoppedAt is not null,
        tier = s.TierCode,
        subscriptionStatus = s.BillingStatus,
        currentPeriodEnd = s.CurrentPeriodEnd,
        unpaid = s.UnpaidCharges.Select(c => new
        {
            id = c.Id,
            periodStart = c.PeriodStart,
            periodEnd = c.PeriodEnd,
            overageMinutes = c.OverageMinutes,
            amountUsd = c.AmountUsd,
        }),
    };

    internal static int StatusFor(string? code) => code switch
    {
        VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        VoiceFailureCodes.Conflict or BillingFailureCodes.ChargeNotOwed => StatusCodes.Status409Conflict,
        BillingFailureCodes.PaymentNotCompleted => StatusCodes.Status402PaymentRequired,
        _ => StatusCodes.Status503ServiceUnavailable,
    };

    private ObjectResult Failure(string? error, string? code) =>
        StatusCode(StatusFor(code), new { error = string.IsNullOrWhiteSpace(error) ? "Billing could not be read." : error, code });
}
