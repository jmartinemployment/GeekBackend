using GeekApplication.Models.Visitors;
using GeekRepository.Auth;
using GeekRepository.Repositories.Visitors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Visitors;

[ApiController]
[Route("repo/visitors/filter-stats")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VisitorFilterStatsController : ControllerBase
{
    private readonly IVisitorRepository _visitors;

    public VisitorFilterStatsController(IVisitorRepository visitors) => _visitors = visitors;

    [HttpPost("drops")]
    public async Task<IActionResult> CountDrop([FromBody] VisitorDropCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A drop is required.");
        return VisitorResults.ToAction(this, await _visitors.CountDropAsync(command, ct));
    }

    [HttpGet("{day}")]
    public async Task<IActionResult> Get(string day, CancellationToken ct) =>
        VisitorResults.ToAction(this, await _visitors.GetFilterDayAsync(day, ct));
}
