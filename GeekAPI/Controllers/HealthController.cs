using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HealthController(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var database = "ok";
        try
        {
            var http = _httpClientFactory.CreateClient("GeekRepository");
            using var response = await http.GetAsync("health", cancellationToken);
            if (!response.IsSuccessStatusCode)
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
