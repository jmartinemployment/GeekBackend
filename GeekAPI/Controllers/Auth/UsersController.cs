using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekAPI.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/users")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _repo;

    public UsersController(IUserRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var result = await _repo.CreateAsync(req);
        return ToResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> FindById(string id)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { error = "Invalid user ID format" });
        var result = await _repo.FindByIdAsync(userId);
        return ToResponse(result);
    }

    [HttpGet("by-email/{email}")]
    public async Task<IActionResult> FindByEmail(string email)
    {
        var result = await _repo.FindByEmailAsync(email);
        return ToResponse(result);
    }

    [HttpGet("by-username/{username}")]
    public async Task<IActionResult> FindByUsername(string username)
    {
        var result = await _repo.FindByUsernameAsync(username);
        return ToResponse(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest req)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { error = "Invalid user ID format" });
        var existing = await _repo.FindByIdAsync(userId);
        if (!existing.IsSuccess)
            return ToResponse(existing);
        var user = existing.Value;
        if (req.Username != null)
            user.Username = req.Username;
        if (req.Email != null)
            user.Email = req.Email;
        if (req.TwoFactorEnabled.HasValue)
            user.TwoFactorEnabled = req.TwoFactorEnabled.Value;
        var result = await _repo.UpdateAsync(user);
        return ToResponse(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { error = "Invalid user ID format" });
        var result = await _repo.DeleteAsync(userId);
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
