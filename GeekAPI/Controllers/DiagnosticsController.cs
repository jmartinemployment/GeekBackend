using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

[ApiController]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok",
            service = "GeekAPI",
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("/hello")]
    public IActionResult Hello()
    {
        return Ok(new
        {
            message = "Hello from GeekAPI",
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("/favicon.ico")]
    public IActionResult Favicon()
    {
        return NoContent();
    }

    [HttpGet("/robots.txt")]
    public IActionResult RobotsTxt()
    {
        return NoContent();
    }
}
