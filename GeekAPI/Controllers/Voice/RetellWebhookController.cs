using System.Text.Json;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// POST /api/receptionist/retell/webhook/{tenantId} (docs/Geek-Receptionist.md section 6): where each tenant's Retell agent posts its
/// call events; RetellProvisioner gives the agent this address. Retell cannot send our API key, so ApiKeyMiddleware lets this prefix
/// through and the X-Retell-Signature over the raw body is what authenticates a delivery: without RETELL_API_KEY nothing is accepted
/// (503), and a delivery whose signature does not match or is more than five minutes old is refused (401). A recorded call answers
/// 204, which is all Retell reads; a store that is down answers 503, so Retell delivers again.
/// </summary>
[ApiController]
[Route(RoutePrefix + "/{tenantId:guid}")]
public sealed class RetellWebhookController : ControllerBase
{
    public const string RoutePrefix = "api/receptionist/retell/webhook";

    /// <summary>A call's transcript is the largest part of a delivery; an hour-long call is well under this.</summary>
    public const int MaxBodyBytes = 5_000_000;

    private readonly RetellOptions _options;
    private readonly RetellCallRecorder _recorder;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetellWebhookController> _logger;

    public RetellWebhookController(RetellOptions options, RetellCallRecorder recorder, TimeProvider clock, ILogger<RetellWebhookController> logger)
    {
        _options = options;
        _recorder = recorder;
        _clock = clock;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(Guid tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("A Retell delivery for tenant {TenantId} was refused: RETELL_API_KEY is not set, so no signature can be checked.", tenantId);
            return Refuse(StatusCodes.Status503ServiceUnavailable, "Retell deliveries cannot be checked.", "retell_not_configured");
        }

        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await Request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        if (!RetellWebhookSignature.IsValid(body, Request.Headers[RetellWebhookSignature.Header], _options.ApiKey, _clock.GetUtcNow()))
        {
            _logger.LogWarning("A Retell delivery for tenant {TenantId} was refused: its signature did not match or was too old.", tenantId);
            return Refuse(StatusCodes.Status401Unauthorized, "The delivery's signature did not match.", "invalid_signature");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            _logger.LogError("A signed Retell delivery for tenant {TenantId} was not JSON.", tenantId);
            return Refuse(StatusCodes.Status400BadRequest, "The delivery is not JSON.", RetellCallFailureCodes.InvalidCall);
        }

        using (doc)
        {
            var recorded = await _recorder.RecordAsync(tenantId, doc.RootElement, ct).ConfigureAwait(false);
            if (recorded.IsSuccess) return NoContent();

            var status = recorded.ErrorCode switch
            {
                RetellCallFailureCodes.InvalidCall or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
                RetellCallFailureCodes.AgentMismatch => StatusCodes.Status403Forbidden,
                VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
                VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
            _logger.LogError("A signed Retell delivery for tenant {TenantId} was not recorded ({Status} {Code}): {Error}", tenantId, status, recorded.ErrorCode, recorded.Error);
            return Refuse(status, recorded.Error!, recorded.ErrorCode!);
        }
    }

    private ObjectResult Refuse(int status, string error, string code) => StatusCode(status, new { error, code });
}
