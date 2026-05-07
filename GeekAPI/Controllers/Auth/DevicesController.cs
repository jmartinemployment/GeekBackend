using GeekRepository.Repositories;
using GeekRepository.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/devices")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _repo;

    public DevicesController(IDeviceRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertDeviceRequest req)
    {
        var result = await _repo.UpsertAsync(req);
        return ToResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> FindById(string id)
    {
        var result = await _repo.FindByIdAsync(id);
        return ToResponse(result);
    }

    [HttpGet("by-user/{userId}")]
    public async Task<IActionResult> FindByUserId(string userId)
    {
        var result = await _repo.FindByUserIdAsync(userId);
        return ToResponse(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDeviceRequest req)
    {
        var result = await _repo.UpdateAsync(id, req);
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
