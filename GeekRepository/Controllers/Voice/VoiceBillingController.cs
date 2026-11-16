using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// /internal/v1/billing/* (docs/Geek-Receptionist.md section 5, item 2): GeekAPI's end-of-cycle billing finds the periods that ended,
/// closes each once, records its charge's outcome and moves the period on, and keeps each tenant's standing: suspended, answering
/// stopped, restored, cancelled, released, an overage the owner paid. Behind X-Internal-Api-Key. The lists of due subscriptions and of
/// suspended tenants are the reads across tenants and answer only ids and period ends; every other route names its tenant.
/// </summary>
[ApiController]
[Route("internal/v1/billing")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VoiceBillingController(IVoiceBillingStore billing) : VoiceStoreController
{
    [HttpPost("subscriptions/due")]
    public async Task<IActionResult> ListDue([FromBody] ListVoiceSubscriptionsDueCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A cut-off time is required.") : ToAction(await billing.ListDueAsync(command, ct));

    [HttpPost("tenants/{tenantId:guid}/periods/close")]
    public async Task<IActionResult> Close(Guid tenantId, [FromBody] CloseVoiceBillingPeriodCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A period is required.") : ToAction(await billing.CloseBillingPeriodAsync(tenantId, command, ct));

    [HttpPost("tenants/{tenantId:guid}/overage-charges/{chargeId:guid}/settle")]
    public async Task<IActionResult> Settle(Guid tenantId, Guid chargeId, [FromBody] SettleVoiceOverageChargeCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An outcome is required.") : ToAction(await billing.SettleOverageChargeAsync(tenantId, chargeId, command, ct));

    [HttpPost("tenants/{tenantId:guid}/periods/advance")]
    public async Task<IActionResult> Advance(Guid tenantId, [FromBody] AdvanceVoiceBillingPeriodCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A period is required.") : ToAction(await billing.AdvanceBillingPeriodAsync(tenantId, command, ct));

    [HttpPut("tenants/{tenantId:guid}/billing-status")]
    public async Task<IActionResult> SetBillingStatus(Guid tenantId, [FromBody] SetVoiceBillingStatusCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A billing status is required.") : ToAction(await billing.SetBillingStatusAsync(tenantId, command, ct));

    [HttpPost("tenants/suspended")]
    public async Task<IActionResult> ListSuspended(CancellationToken ct) => ToAction(await billing.ListSuspendedAsync(ct));

    [HttpGet("tenants/{tenantId:guid}/standing")]
    public async Task<IActionResult> GetStanding(Guid tenantId, CancellationToken ct) => ToAction(await billing.GetStandingAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid tenantId, CancellationToken ct) => ToAction(await billing.SuspendTenantAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid tenantId, CancellationToken ct) => ToAction(await billing.RestoreTenantAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/answering-stopped")]
    public async Task<IActionResult> AnsweringStopped(Guid tenantId, CancellationToken ct) => ToAction(await billing.MarkAnsweringStoppedAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid tenantId, CancellationToken ct) => ToAction(await billing.CancelTenantAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/telephony-released")]
    public async Task<IActionResult> TelephonyReleased(Guid tenantId, CancellationToken ct) => ToAction(await billing.MarkTelephonyReleasedAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/overage-charges/{chargeId:guid}/collect")]
    public async Task<IActionResult> Collect(Guid tenantId, Guid chargeId, [FromBody] CollectVoiceOverageChargeCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A payment is required.") : ToAction(await billing.CollectOverageChargeAsync(tenantId, chargeId, command, ct));
}
