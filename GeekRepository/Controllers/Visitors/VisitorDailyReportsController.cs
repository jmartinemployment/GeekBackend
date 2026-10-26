using GeekApplication.Models.Visitors;
using GeekRepository.Auth;
using GeekRepository.Repositories.Visitors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Visitors;

[ApiController]
[Route("repo/visitors/daily-reports")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VisitorDailyReportsController : ControllerBase
{
    private readonly IVisitorRepository _visitors;

    public VisitorDailyReportsController(IVisitorRepository visitors) => _visitors = visitors;

    [HttpPost("{date}/claim")]
    public async Task<IActionResult> Claim(string date, [FromBody] VisitorDailyReportClaimCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A claim is required.");
        var result = await _visitors.ClaimDailyReportAsync(date, command, ct);
        return result.IsSuccess
            ? Ok(new VisitorClaimResult { Claimed = result.Value })
            : VisitorResults.Failed(this, result.Error);
    }

    [HttpPost("{date}/built")]
    public async Task<IActionResult> Built(string date, [FromBody] VisitorDailyReportBuiltCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A built mark is required.");
        return VisitorResults.ToAction(this, await _visitors.MarkDailyReportBuiltAsync(date, command, ct));
    }

    [HttpPost("{date}/release")]
    public async Task<IActionResult> Release(string date, [FromBody] VisitorDailyReportReleaseCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A release is required.");
        return VisitorResults.ToAction(this, await _visitors.ReleaseDailyReportAsync(date, command, ct));
    }

    [HttpGet("{date}")]
    public async Task<IActionResult> Get(string date, CancellationToken ct) =>
        VisitorResults.ToAction(this, await _visitors.GetDailyReportAsync(date, ct));
}
