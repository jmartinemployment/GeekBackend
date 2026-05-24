using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.OpenIddict;

[ApiController]
[Route("repo/openiddict/applications")]
public sealed class OidcApplicationsController : ControllerBase
{
    private readonly IOpenIddictApplicationRepository _repo;
    private readonly ILogger<OidcApplicationsController> _logger;

    public OidcApplicationsController(
        IOpenIddictApplicationRepository repo,
        ILogger<OidcApplicationsController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken cancellationToken) =>
        ToResponse(await _repo.CountAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? count, [FromQuery] int? offset, CancellationToken cancellationToken) =>
        ToResponse(await _repo.ListAsync(count, offset, cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken) =>
        ToResponse(await _repo.FindByIdAsync(id, cancellationToken));

    [HttpGet("by-client-id/{clientId}")]
    public async Task<IActionResult> GetByClientId(string clientId, CancellationToken cancellationToken) =>
        ToResponse(await _repo.FindByClientIdAsync(clientId, cancellationToken));

    [HttpGet("by-redirect-uri")]
    public async Task<IActionResult> GetByRedirectUri([FromQuery] string uri, CancellationToken cancellationToken) =>
        ToResponse(await _repo.FindByRedirectUriAsync(uri, cancellationToken));

    [HttpGet("by-post-logout-redirect-uri")]
    public async Task<IActionResult> GetByPostLogoutRedirectUri([FromQuery] string uri, CancellationToken cancellationToken) =>
        ToResponse(await _repo.FindByPostLogoutRedirectUriAsync(uri, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GeekOpenIddictApplication application, CancellationToken cancellationToken) =>
        ToResponse(await _repo.CreateAsync(application, cancellationToken));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] GeekOpenIddictApplication application, CancellationToken cancellationToken)
    {
        application.Id = id;
        return ToResponse(await _repo.UpdateAsync(application, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken) =>
        ToResponse(await _repo.DeleteAsync(id, cancellationToken));

    private IActionResult ToResponse<T>(Result<T> result)
    {
        if (result.Status == ResultStatus.Failure)
            _logger.LogError("OpenIddict applications operation failed: {Error}", result.Error);

        return result.Status switch
        {
            ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
            ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
            ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
            _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
        };
    }
}
