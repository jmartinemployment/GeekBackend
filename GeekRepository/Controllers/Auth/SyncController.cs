using GeekApplication.Interfaces;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncRepository _repo;

    public SyncController(ISyncRepository repo) => _repo = repo;

    [HttpPost("enqueue")]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueSyncRequest req) =>
        ToResponse(await _repo.EnqueueAsync(req.UserId, req.TargetDeviceId, req.Payload));

    [HttpGet("{queueId}")]
    public async Task<IActionResult> FindById(Guid queueId) =>
        ToResponse(await _repo.FindByIdAsync(queueId));

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] Guid userId, [FromQuery] Guid deviceId) =>
        ToResponse(await _repo.GetPendingAsync(userId, deviceId));

    [HttpPost("{queueId}/processed")]
    public async Task<IActionResult> MarkProcessed(Guid queueId) =>
        ToResponse(await _repo.MarkProcessedAsync(queueId));

    [HttpPost("{queueId}/failed")]
    public async Task<IActionResult> MarkFailed(Guid queueId, [FromBody] MarkFailedRequest req) =>
        ToResponse(await _repo.MarkFailedAsync(queueId, req.ErrorMessage));

    [HttpPost("conflicts")]
    public async Task<IActionResult> LogConflict([FromBody] LogConflictRequest req) =>
        ToResponse(await _repo.LogConflictAsync(req.UserId, req.DeviceId, req.FieldName, req.ExpectedValue, req.ActualValue));

    [HttpGet("conflicts/{userId}")]
    public async Task<IActionResult> GetConflicts(Guid userId) =>
        ToResponse(await _repo.GetConflictsAsync(userId));

    [HttpPost("conflicts/{conflictId}/resolve")]
    public async Task<IActionResult> ResolveConflict(Guid conflictId, [FromBody] ResolveConflictRequest req) =>
        ToResponse(await _repo.ResolveConflictAsync(conflictId, req.Resolution));

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}

public sealed record EnqueueSyncRequest(Guid UserId, Guid TargetDeviceId, string Payload);
public sealed record MarkFailedRequest(string ErrorMessage);
public sealed record LogConflictRequest(Guid UserId, Guid DeviceId, string FieldName, string ExpectedValue, string ActualValue);
public sealed record ResolveConflictRequest(string Resolution);
