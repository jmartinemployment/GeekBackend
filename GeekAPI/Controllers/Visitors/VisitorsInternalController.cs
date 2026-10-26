using GeekAPI.HttpClients;
using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Visitors;

/// <summary>
/// The site's server side posts events here, and the owner reads the daily report here, both
/// with X-API-Key. ApiKeyMiddleware guards every <c>/api/*/internal/</c> route, so a request
/// without the key never reaches this class.
/// </summary>
[ApiController]
[Route("api/visitors/internal")]
public sealed class VisitorsInternalController : ControllerBase
{
    private readonly VisitorEventProcessor _processor;
    private readonly VisitorDailyReportComposer _composer;
    private readonly IVisitorRepositoryClient _repo;

    public VisitorsInternalController(VisitorEventProcessor processor, VisitorDailyReportComposer composer, IVisitorRepositoryClient repo)
    {
        _processor = processor;
        _composer = composer;
        _repo = repo;
    }

    [HttpPost("events")]
    public async Task<IActionResult> Events([FromBody] VisitorPulseEvent body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { error = "An event is required." });
        return Map(await _processor.ProcessAsync(body, ct));
    }

    [HttpPost("identify")]
    public async Task<IActionResult> Identify([FromBody] VisitorIdentifyRequest body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { error = "An identity is required." });
        return Map(await _processor.IdentifyAsync(body, ct));
    }

    [HttpPost("drops")]
    public async Task<IActionResult> Drops([FromBody] VisitorDropRequest body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { error = "A drop is required." });
        return Map(await _processor.CountDropAsync(body, ct));
    }

    /// <summary>The day's report as text, composed from its sessions, for any day inside the 180-day retention.</summary>
    [HttpGet("reports/{date}")]
    public async Task<IActionResult> Report(string date, CancellationToken ct)
    {
        if (!VisitorClock.TryParseDayKey(date, out var day)) return BadRequest(new { error = "date must be yyyy-MM-dd." });
        var composed = await _composer.ComposeAsync(day, ct);
        if (!composed.IsSuccess)
            return StatusCode(StatusCodes.Status502BadGateway, new { error = composed.Error });
        return Content(composed.Value!.Body, "text/plain; charset=utf-8");
    }

    /// <summary>What the 07:00 build recorded for the day: when it was claimed and built, and its totals.</summary>
    [HttpGet("reports/{date}/summary")]
    public async Task<IActionResult> Summary(string date, CancellationToken ct)
    {
        if (!VisitorClock.TryParseDayKey(date, out _)) return BadRequest(new { error = "date must be yyyy-MM-dd." });
        var stored = await _repo.GetDailyReportAsync(date, ct);
        if (stored.IsSuccess) return Ok(stored.Value);
        if (stored.Status == ResultStatus.NotFound) return NotFound(new { error = "No build recorded for that date." });
        return StatusCode(StatusCodes.Status502BadGateway, new { error = stored.Error });
    }

    /// <summary>One of the day's three CSV files, composed from its sessions.</summary>
    [HttpGet("reports/{date}/{kind}.csv")]
    public async Task<IActionResult> ReportCsv(string date, string kind, CancellationToken ct)
    {
        if (!VisitorClock.TryParseDayKey(date, out var day)) return BadRequest(new { error = "date must be yyyy-MM-dd." });
        if (kind is not ("sessions" or "pages" or "interactions"))
            return NotFound(new { error = "kind must be sessions, pages or interactions." });

        var composed = await _composer.ComposeAsync(day, ct);
        if (!composed.IsSuccess)
            return StatusCode(StatusCodes.Status502BadGateway, new { error = composed.Error });

        var report = composed.Value!;
        var bytes = kind switch
        {
            "sessions" => report.SessionsCsv,
            "pages" => report.PagesCsv,
            _ => report.InteractionsCsv,
        };
        return File(bytes, "text/csv; charset=utf-8", $"{kind}-{report.DateKey}.csv");
    }

    private IActionResult Map(Result result)
    {
        if (result.IsSuccess) return Accepted();
        return result.ErrorCode switch
        {
            VisitorEventProcessor.BadRequest => BadRequest(new { error = result.Error }),
            VisitorEventProcessor.NotFound => NotFound(new { error = result.Error }),
            _ => StatusCode(StatusCodes.Status502BadGateway, new { error = result.Error }),
        };
    }
}
