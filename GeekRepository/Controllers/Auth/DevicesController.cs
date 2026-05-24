using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekRepository.Dtos;
using GeekApplication.Results;
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
        return ToResponse(await _repo.UpsertAsync(parsedUserId, req));
    }

    [HttpPost("{userId}/register")]
    public async Task<IActionResult> Register(string userId, [FromBody] RegisterDeviceOauthRequest req)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.RegisterAsync(parsedUserId, req));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> FindById(string id)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.FindByIdAsync(deviceId));
    }

    [HttpGet("by-user/{userId}")]
    public async Task<IActionResult> FindByUserId(string userId)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.FindByUserIdAsync(parsedUserId));
    }

    [HttpGet("by-user/{userId}/active")]
    public async Task<IActionResult> GetActive(string userId)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.GetActiveDevicesAsync(parsedUserId));
    }

    [HttpGet("by-fingerprint/{userId}/{fingerprint}")]
    public async Task<IActionResult> FindByFingerprint(string userId, string fingerprint) =>
        !Guid.TryParse(userId, out var parsedUserId)
            ? BadRequest(new { error = "Invalid user ID format" })
            : ToResponse(await _repo.FindByFingerprintAsync(parsedUserId, fingerprint));

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDeviceRequest req)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        var existing = await _repo.FindByIdAsync(deviceId);
        if (!existing.IsSuccess)
            return ToResponse(existing);
        var device = existing.Value!;
        if (req.DeviceName is not null)
            device.DeviceName = req.DeviceName;
        if (req.IsTrusted.HasValue)
            device.IsTrusted = req.IsTrusted.Value;
        return ToResponse(await _repo.UpdateAsync(device));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.DeleteAsync(deviceId));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(string id)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.RevokeAsync(deviceId));
    }

    [HttpPost("{id}/trust")]
    public async Task<IActionResult> Trust(string id, [FromBody] TrustDeviceRequest? req)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.TrustAsync(deviceId, req?.Days ?? 30));
    }

    [HttpPost("{id}/challenge")]
    public async Task<IActionResult> Challenge(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.IssueChallengeAsync(deviceId, cancellationToken));
    }

    [HttpPost("{id}/verify")]
    public async Task<IActionResult> Verify(string id, [FromBody] VerifyDeviceChallengeRequest req, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = "Invalid device ID format" });
        return ToResponse(await _repo.VerifyChallengeAsync(
            deviceId,
            req.Nonce,
            req.Signature,
            req.PublicKeyPem,
            cancellationToken));
    }

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };

    public sealed record TrustDeviceRequest(int? Days);
    public sealed record VerifyDeviceChallengeRequest(string Nonce, string Signature, string? PublicKeyPem);
}
