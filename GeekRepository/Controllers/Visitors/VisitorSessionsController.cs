using GeekApplication.Models.Visitors;
using GeekApplication.Results;
using GeekRepository.Auth;
using GeekRepository.Repositories.Visitors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Visitors;

/// <summary>Visitor sessions: one write per route, landed once under the row's version, the session after the write as the answer.</summary>
[ApiController]
[Route("repo/visitors/sessions")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VisitorSessionsController : ControllerBase
{
    private readonly IVisitorRepository _visitors;

    public VisitorSessionsController(IVisitorRepository visitors) => _visitors = visitors;

    [HttpPost("page-view")]
    public async Task<IActionResult> PageView([FromBody] VisitorPageViewCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A page view is required.");
        return VisitorResults.ToAction(this, await _visitors.PageViewAsync(command, ct));
    }

    [HttpPost("{id:guid}/page-end")]
    public async Task<IActionResult> PageEnd(Guid id, [FromBody] VisitorPageEndCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A page end is required.");
        return VisitorResults.ToAction(this, await _visitors.PageEndAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/clicks")]
    public async Task<IActionResult> Click(Guid id, [FromBody] VisitorClickCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A click is required.");
        return VisitorResults.ToAction(this, await _visitors.ClickAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/vitals")]
    public async Task<IActionResult> Vitals(Guid id, [FromBody] VisitorVitalsCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A vital is required.");
        return VisitorResults.ToAction(this, await _visitors.VitalsAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/scheduler")]
    public async Task<IActionResult> SchedulerStep(Guid id, [FromBody] VisitorSchedulerStepCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A scheduler step is required.");
        return VisitorResults.ToAction(this, await _visitors.SchedulerStepAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/contact")]
    public async Task<IActionResult> Contact(Guid id, [FromBody] VisitorContactCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A contact submission is required.");
        return VisitorResults.ToAction(this, await _visitors.ContactSubmittedAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/browser-geo")]
    public async Task<IActionResult> BrowserGeo(Guid id, [FromBody] VisitorBrowserGeoCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A position is required.");
        return VisitorResults.ToAction(this, await _visitors.BrowserGeoAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/score")]
    public async Task<IActionResult> Score(Guid id, [FromBody] VisitorScoreCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A score is required.");
        return VisitorResults.ToAction(this, await _visitors.ScoreAsync(id, command, ct));
    }

    /// <summary>Marks the session as having reached the intent threshold. Claimed is true for the one call that set it.</summary>
    [HttpPost("{id:guid}/intent-reached")]
    public async Task<IActionResult> IntentReached(Guid id, [FromBody] VisitorIntentReachedCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A time is required.");
        var result = await _visitors.MarkIntentReachedAsync(id, command, ct);
        if (result.IsSuccess) return Ok(new VisitorClaimResult { Claimed = result.Value });
        if (result.Status == ResultStatus.NotFound) return NotFound(result.Error);
        return VisitorResults.Failed(this, result.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        VisitorResults.ToAction(this, await _visitors.GetSessionAsync(id, ct));

    /// <summary>Sessions started in [fromUtc, toUtc), paged on (startedAt, sequence).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] DateTime? afterStartedAt,
        [FromQuery] long? afterSequence,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (fromUtc == default || toUtc == default || toUtc <= fromUtc)
            return BadRequest("fromUtc and toUtc are required and toUtc must follow fromUtc.");
        return VisitorResults.ToAction(this, await _visitors.ListSessionsAsync(fromUtc, toUtc, afterStartedAt, afterSequence, limit, ct));
    }

    [HttpGet("for-visitor")]
    public async Task<IActionResult> ListForVisitor(
        [FromQuery] Guid visitorId,
        [FromQuery] DateTime sinceUtc,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (visitorId == Guid.Empty || sinceUtc == default)
            return BadRequest("visitorId and sinceUtc are required.");
        return VisitorResults.ToAction(this, await _visitors.ListSessionsForVisitorAsync(visitorId, sinceUtc, limit, ct));
    }
}

/// <summary>
/// Result to HTTP, one way for all five visitor controllers: a value is 200, NotFound is 404, a
/// refused input is 400, and a store that is down or unconfigured is 503.
/// </summary>
internal static class VisitorResults
{
    public static IActionResult ToAction<T>(ControllerBase controller, Result<T> result)
    {
        if (result.IsSuccess) return controller.Ok(result.Value);
        if (result.Status == ResultStatus.NotFound) return controller.NotFound(result.Error);
        return Failed(controller, result.Error);
    }

    public static IActionResult ToAction(ControllerBase controller, Result result)
    {
        if (result.IsSuccess) return controller.NoContent();
        if (result.ErrorCode == "not_found") return controller.NotFound(result.Error);
        if (result.ErrorCode == "bad_request") return controller.BadRequest(result.Error);
        return Failed(controller, result.Error);
    }

    public static IActionResult Failed(ControllerBase controller, string? error)
    {
        var message = error ?? "Visitor store failed.";
        // A refused input names what was missing; a store failure says the store failed.
        var refused = message.Contains("required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("must", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("Unknown", StringComparison.Ordinal);
        return refused
            ? controller.BadRequest(message)
            : controller.StatusCode(StatusCodes.Status503ServiceUnavailable, message);
    }
}
