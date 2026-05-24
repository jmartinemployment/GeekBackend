using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly IUserRepository _users;

    public HealthController(IUserRepository users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var database = "ok";
        try
        {
            var ping = await _users.FindByIdAsync(Guid.Empty);
            if (ping.Status == GeekApplication.Results.ResultStatus.Failure)
                database = "error";
        }
        catch
        {
            database = "error";
        }

        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.UtcNow,
            service = "GeekAPI",
            database
        });
    }
}
