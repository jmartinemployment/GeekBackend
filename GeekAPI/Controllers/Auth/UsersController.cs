using GeekRepository.Repositories;
using GeekRepository.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/users")]
public class UsersController : ControllerBase
{
    private readonly IUserAuthRepository _repo;

    public UsersController(IUserAuthRepository repo)
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
        var result = await _repo.FindByIdAsync(id);
        return ToResponse(result);
    }

    [HttpGet("by-email/{email}")]
    public async Task<IActionResult> FindByEmail(string email)
    {
        var result = await _repo.FindByEmailAsync(email);
        return ToResponse(result);
    }

    [HttpGet("by-slack/{slackUserId}")]
    public async Task<IActionResult> FindBySlackId(string slackUserId)
    {
        var result = await _repo.FindBySlackIdAsync(slackUserId);
        return ToResponse(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest req)
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
