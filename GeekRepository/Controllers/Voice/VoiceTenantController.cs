using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// /internal/v1/tenants/{tenantId}/* (docs/Geek-Receptionist.md section 6): one tenant's telephony, settings, calendar connection,
/// knowledge, calls and appointments. Every route names its tenant and the store never reads across tenants. Behind
/// X-Internal-Api-Key. GeekAPI takes the tenant from the signed-in user's token before it calls here; this service trusts the id
/// it is given because only GeekAPI holds the key.
/// </summary>
[ApiController]
[Route("internal/v1/tenants/{tenantId:guid}")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VoiceTenantController(IVoiceStore voice) : VoiceStoreController
{
    [HttpGet("telephony")]
    public async Task<IActionResult> GetTelephony(Guid tenantId, CancellationToken ct) =>
        ToAction(await voice.GetTelephonyAsync(tenantId, ct));

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(Guid tenantId, CancellationToken ct) =>
        ToAction(await voice.GetConfigAsync(tenantId, ct));

    [HttpPut("config")]
    public async Task<IActionResult> SaveConfig(Guid tenantId, [FromBody] VoiceReceptionistConfig config, CancellationToken ct) =>
        config is null ? BadRequest("Settings are required.") : ToAction(await voice.SaveConfigAsync(tenantId, config, ct));

    [HttpGet("mcp/{provider}")]
    public async Task<IActionResult> GetMcpConnection(Guid tenantId, string provider, CancellationToken ct) =>
        ToAction(await voice.GetMcpConnectionAsync(tenantId, provider, ct));

    [HttpPut("mcp/{provider}")]
    public async Task<IActionResult> SaveMcpConnection(Guid tenantId, string provider, [FromBody] SaveVoiceMcpConnectionCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A connection is required.") : ToAction(await voice.SaveMcpConnectionAsync(tenantId, provider, command, ct));

    [HttpPost("knowledge/documents")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> CreateKnowledgeDocument(Guid tenantId, [FromBody] CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A document is required.") : ToAction(await voice.CreateKnowledgeDocumentAsync(tenantId, command, ct));

    [HttpPost("knowledge/search")]
    public async Task<IActionResult> SearchKnowledge(Guid tenantId, [FromBody] SearchVoiceKnowledgeCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A search is required.") : ToAction(await voice.SearchKnowledgeAsync(tenantId, command, ct));

    [HttpPost("calls")]
    public async Task<IActionResult> UpsertCall(Guid tenantId, [FromBody] UpsertVoiceCallCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A call is required.") : ToAction(await voice.UpsertCallAsync(tenantId, command, ct));

    [HttpPost("appointments")]
    public async Task<IActionResult> CreateAppointment(Guid tenantId, [FromBody] CreateVoiceAppointmentCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An appointment is required.") : ToAction(await voice.CreateAppointmentAsync(tenantId, command, ct));
}
