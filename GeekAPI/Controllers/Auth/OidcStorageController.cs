using GeekRepository.Repositories;
using GeekRepository.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/oidc-storage")]
public class OidcStorageController : ControllerBase
{
    private readonly IOidcStorageRepository _repo;

    public OidcStorageController(IOidcStorageRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] OidcStorageUpsertRequest req)
    {
        var result = await _repo.UpsertAsync(req.Id, req.Kind, req.Payload, req.ExpiresIn);
        return ToResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Find(string id)
    {
        var result = await _repo.FindAsync(id);
        return ToResponse(result);
    }

    [HttpGet("by-uid/{uid}")]
    public async Task<IActionResult> FindByUid(string uid)
    {
        var result = await _repo.FindByUidAsync(uid);
        return ToResponse(result);
    }

    [HttpGet("by-usercode/{userCode}")]
    public async Task<IActionResult> FindByUserCode(string userCode)
    {
        var result = await _repo.FindByUserCodeAsync(userCode);
        return ToResponse(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Destroy(string id)
    {
        var result = await _repo.DestroyAsync(id);
        return ToResponse(result);
    }

    [HttpPost("{id}/consume")]
    public async Task<IActionResult> Consume(string id)
    {
        var result = await _repo.ConsumeAsync(id);
        return ToResponse(result);
    }

    [HttpPost("revoke-by-grant/{grantId}")]
    public async Task<IActionResult> RevokeByGrantId(string grantId)
    {
        var result = await _repo.RevokeByGrantIdAsync(grantId);
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

public class OidcStorageUpsertRequest
{
    public string Id { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public Dictionary<string, object> Payload { get; set; } = [];
    public int? ExpiresIn { get; set; }
}
