using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// /internal/v1/billing/* (docs/Geek-Receptionist.md section 5, item 2): GeekAPI's end-of-cycle billing finds the periods that ended,
/// closes each once, records its charge's outcome and moves the period on. Behind X-Internal-Api-Key. The list of due subscriptions
/// is the one read across tenants and answers only ids and period ends; every other route names its tenant.
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
}
