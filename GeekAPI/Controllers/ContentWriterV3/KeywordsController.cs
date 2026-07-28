using GeekAPI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/keywords")]
public class KeywordsController : ControllerBase
{
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<KeywordsController> _logger;

    public KeywordsController(
        ICurrentUserContext currentUser,
        ILogger<KeywordsController> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<object>> GetByClientId(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        _logger.LogInformation("User {UserId} fetching keywords for client {ClientId}",
            _currentUser.UserId, clientId);

        // TODO: Implement keyword fetching
        return Ok(new List<object>());
    }
}
