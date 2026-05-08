using GeekApplication.Interfaces;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using GeekAPI.Dtos;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/pending-verifications")]
public class PendingVerificationsController : ControllerBase
{
    private readonly IPendingVerificationRepository _repo;

    public PendingVerificationsController(IPendingVerificationRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertPendingVerificationRequest req)
    {
        var result = await _repo.UpsertAsync(req.VerificationCode, req.ExpiresAt);
        return ToResponse(result);
    }

    [HttpGet("by-email/{email}")]
    public async Task<IActionResult> FindByEmail(string email)
    {
        var result = await _repo.FindByEmailAsync(email);
        return ToResponse(result);
    }

    [HttpPatch("{id}/increment-attempts")]
    public async Task<IActionResult> IncrementAttempts(string id)
    {
        var result = await _repo.IncrementAttemptsAsync(id);
        return ToResponse(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _repo.DeleteAsync(id);
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
