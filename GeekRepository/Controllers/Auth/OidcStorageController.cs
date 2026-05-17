using System.Text.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/auth/oidc-storage")]
public class OidcStorageController : ControllerBase
{
    private readonly IOidcStorageRepository _repo;

    public OidcStorageController(IOidcStorageRepository repo) => _repo = repo;

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] OidcStorageUpsertRequest req)
    {
        var storage = new OidcStorage
        {
            Id = req.Id,
            Kind = req.Kind,
            Payload = JsonSerializer.Serialize(req.Payload),
            ExpiresAt = req.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddMinutes(req.ExpiresIn.Value)
                : DateTime.UtcNow.AddMinutes(10),
            UserCode = req.UserCode,
            TokenHash = req.TokenHash,
            Uid = req.Uid,
            GrantId = req.GrantId,
            CreatedAt = DateTime.UtcNow
        };
        var result = await _repo.UpsertAsync(storage);
        return ToResponse(result);
    }

    [HttpGet("{kind}/{key}")]
    public async Task<IActionResult> Find(string kind, string key)
    {
        var result = await _repo.FindAsync(kind, key);
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

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
