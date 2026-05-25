using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekAPI.Dtos;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceOauthRepository _repo;

    public DevicesController(IDeviceOauthRepository repo) => _repo = repo;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceOauthRequest req)
    {
        var userId = GetUserId();
        return ToResponse(await _repo.RegisterAsync(userId, req));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertDeviceRequest req) =>
        ToResponse(await _repo.UpsertAsync(GetUserId(), req));

    [HttpGet("{id}")]
    public async Task<IActionResult> FindById(string id) =>
        !Guid.TryParse(id, out var deviceId)
            ? BadRequest(new { error = "Invalid device ID format" })
            : ToResponse(await _repo.FindByIdAsync(deviceId));

    [HttpGet]
    public async Task<IActionResult> ListMine() =>
        ToResponse(await _repo.FindByUserIdAsync(GetUserId()));

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
    public async Task<IActionResult> Delete(string id) =>
        !Guid.TryParse(id, out var deviceId)
            ? BadRequest(new { error = "Invalid device ID format" })
            : ToResponse(await _repo.DeleteAsync(deviceId));

    [HttpPost("{id}/challenge")]
    [EnableRateLimiting("device-challenge")]
    public async Task<IActionResult> Challenge(string id, CancellationToken cancellationToken) =>
        !Guid.TryParse(id, out var deviceId)
            ? BadRequest(new { error = "Invalid device ID format" })
            : ToResponse(await _repo.IssueChallengeAsync(deviceId, cancellationToken));

    [HttpPost("{id}/verify")]
    public async Task<IActionResult> Verify(
        string id,
        [FromBody] VerifyDeviceChallengeRequest req,
        CancellationToken cancellationToken) =>
        !Guid.TryParse(id, out var deviceId)
            ? BadRequest(new { error = "Invalid device ID format" })
            : ToResponse(await _repo.VerifyChallengeAsync(
                deviceId,
                req.Nonce,
                req.Signature,
                req.PublicKeyPem,
                cancellationToken));

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException());

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => BadRequest(new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };

    public sealed record VerifyDeviceChallengeRequest(string Nonce, string Signature, string? PublicKeyPem);
}
