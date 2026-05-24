using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.OpenIddict;

[ApiController]
[Route("repo/openiddict/authorizations")]
public sealed class OidcAuthorizationsController : ControllerBase
{
    private readonly IOpenIddictAuthorizationRepository _repo;

    public OidcAuthorizationsController(IOpenIddictAuthorizationRepository repo) => _repo = repo;

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken cancellationToken) =>
        ToResponse(await _repo.CountAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? count, [FromQuery] int? offset, CancellationToken cancellationToken) =>
        ToResponse(await _repo.ListAsync(count, offset, cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken) =>
        ToResponse(await _repo.FindByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        ToResponse(await _repo.CreateAsync(authorization, cancellationToken));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken)
    {
        authorization.Id = id;
        return ToResponse(await _repo.UpdateAsync(authorization, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken) =>
        ToResponse(await _repo.DeleteAsync(id, cancellationToken));

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
