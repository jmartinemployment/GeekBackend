using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// /internal/v1/onboarding/tenants (docs/Geek-Receptionist.md section 6): a business signs up and, once it has paid and its
/// telephony exists, is activated. Behind X-Internal-Api-Key. The status comes from the failure code, never from its wording.
/// </summary>
[ApiController]
[Route("internal/v1/onboarding/tenants")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VoiceOnboardingController(IVoiceStore voice, IVoiceSignupAnswerStore answers) : VoiceStoreController
{
    /// <summary>The answers of a visitor who said yes to an excluded industry and was refused: stored with no tenant, kept forever.</summary>
    [HttpPost("/internal/v1/onboarding/industry-refusals")]
    public async Task<IActionResult> RecordIndustryRefusal([FromBody] VoiceIndustryAnswerSet set, CancellationToken ct) =>
        set is null ? BadRequest("The answers are required.") : ToAction(await answers.RecordIndustryRefusalAsync(set, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVoiceTenantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A tenant is required.") : ToAction(await voice.CreateTenantAsync(command, ct));

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct) =>
        ToAction(await voice.GetTenantAsync(tenantId, ct));

    [HttpPost("{tenantId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid tenantId, [FromBody] ActivateVoiceTenantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An activation is required.") : ToAction(await voice.ActivateTenantAsync(tenantId, command, ct));
}
