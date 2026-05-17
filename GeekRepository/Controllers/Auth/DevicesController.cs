using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/auth/devices")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceOauthRepository _repo;

    public DevicesController(IDeviceOauthRepository repo) => _repo = repo;

    [HttpPost("{userId}")]
    public async Task<IActionResult> Upsert(string userId, [FromBody] UpsertDeviceRequest req)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        var result = await _repo.UpsertAsync(parsedUserId, req);
        return ToResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> FindById(string id)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        var result = await _repo.FindByIdAsync(deviceId);
        return ToResponse(result);
    }

    [HttpGet("by-user/{userId}")]
    public async Task<IActionResult> FindByUserId(string userId)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        var result = await _repo.FindByUserIdAsync(parsedUserId);
        return ToResponse(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDeviceRequest req)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        var existing = await _repo.FindByIdAsync(deviceId);
        if (!existing.IsSuccess)
            return ToResponse(existing);
        var device = existing.Value!;
        if (req.DeviceName != null) device.DeviceName = req.DeviceName;
        if (req.IsTrusted.HasValue) device.IsTrusted = req.IsTrusted.Value;
        var result = await _repo.UpdateAsync(device);
        return ToResponse(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        var result = await _repo.DeleteAsync(deviceId);
        return ToResponse(result);
    }

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
