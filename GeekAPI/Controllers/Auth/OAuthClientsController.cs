using GeekRepository.Repositories;
using GeekRepository.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/clients")]
public class OAuthClientsController : ControllerBase
{
    private readonly IOAuthClientRepository _repo;

    public OAuthClientsController(IOAuthClientRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOAuthClientRequest req)
    {
        var result = await _repo.CreateAsync(req);
        return ToResponse(result);
    }

    [HttpGet("by-id/{clientId}")]
    public async Task<IActionResult> FindByClientId(string clientId)
    {
        var result = await _repo.FindByClientIdAsync(clientId);
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
