using GeekAPI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ReconciliationController> _logger;

    public ReconciliationController(
        ICurrentUserContext currentUser,
        ILogger<ReconciliationController> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<object>> GetPendingByClientId(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        _logger.LogInformation("User {UserId} fetching reconciliation proposals for client {ClientId}",
            _currentUser.UserId, clientId);

        // TODO: Implement reconciliation fetching
        return Ok(new List<object>());
    }
}
