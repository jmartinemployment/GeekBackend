using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekApplication.Entities;
using GeekRepository.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/auth/pending-verifications")]
public class PendingVerificationsController : ControllerBase
{
    private readonly IPendingVerificationRepository _repo;

    public PendingVerificationsController(IPendingVerificationRepository repo) => _repo = repo;

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

    [HttpPost("sessions")]
    public async Task<IActionResult> StoreSession([FromBody] StorePendingSessionRequest req) =>
        ToResponse(await _repo.StorePendingSessionAsync(req.UserId, req.SessionCode, req.DeviceFingerprint, req.ExpiresAt));

    [HttpGet("sessions/{sessionCode}")]
    public async Task<IActionResult> GetSession(string sessionCode) =>
        ToResponse(await _repo.GetPendingSessionAsync(sessionCode));

    [HttpPost("sessions/{sessionCode}/complete")]
    public async Task<IActionResult> CompleteSession(string sessionCode) =>
        ToResponse(await _repo.CompletePendingSessionAsync(sessionCode));

    [HttpPost("sessions/{sessionCode}/expire")]
    public async Task<IActionResult> ExpireSession(string sessionCode) =>
        ToResponse(await _repo.ExpirePendingSessionAsync(sessionCode));

    [HttpPatch("sessions/{sessionCode}/increment-attempt")]
    public async Task<IActionResult> IncrementSessionAttempt(string sessionCode) =>
        ToResponse(await _repo.IncrementAttemptAsync(sessionCode));

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup() =>
        ToResponse(await _repo.CleanupExpiredAsync());

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
