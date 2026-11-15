using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The owner's own check that the calendar they connected works (docs/Geek-Receptionist.md section 6). It runs list_calendars for the
/// owner's tenant through the same path a phone call will use, so a tenant whose connection is dead learns it here and not from a missed
/// booking. The tenant comes from the owner's tenant_id claim and a URL naming another tenant is refused (403). A refusal is answered
/// with its message and its code.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class CalendarController(CalendarTools calendar) : ControllerBase
{
    [HttpGet("api/v1/tenants/{tenantId:guid}/calendar/check")]
    public async Task<IActionResult> Check(Guid tenantId, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var result = await calendar.CallAsync(tenantId, GoogleCalendarOptions.Provider, "list_calendars", null, ct);
        if (result.IsSuccess) return Ok(new { connected = true, calendars = result.Value!.StructuredContent ?? result.Value.Content });

        var error = string.IsNullOrWhiteSpace(result.Error) ? "The calendar could not be checked." : result.Error;
        return StatusCode(StatusFor(result.ErrorCode), new { error, code = result.ErrorCode });
    }

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        McpFailureCodes.ProviderNotSupported
            or McpFailureCodes.ToolNotAllowed
            or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        McpFailureCodes.CalendarNotConnected or VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        McpFailureCodes.ReauthorizationRequired or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        McpFailureCodes.ToolError => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
