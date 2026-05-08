using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using GeekAPI.Dtos;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditRepository _repo;

    public AuditController(IAuditRepository repo)
    {
        _repo = repo;
    }

    [HttpPost("logs")]
    public async Task<IActionResult> CreateLog([FromBody] CreateAuditLogRequest req)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = req.UserId,
            EventType = "manual_log",
            Description = req.Details ?? "",
            IpAddress = req.IpAddress,
            IsSuccess = true,
            CreatedAt = DateTime.UtcNow
        };
        var result = await _repo.CreateLogAsync(log);
        return ToResponse(result);
    }

    [HttpPost("circuit-resets")]
    public async Task<IActionResult> CreateCircuitReset([FromBody] CreateCircuitResetRequest req)
    {
        var result = await _repo.CreateCircuitResetAsync(req.UserId, req.FailureCount);
        return ToResponse(result);
    }

    private IActionResult ToResponse<T>(Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
            ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
            ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
            _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
        };
    }
}
