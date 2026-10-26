using GeekApplication.Models.Visitors;
using GeekRepository.Auth;
using GeekRepository.Repositories.Visitors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Visitors;

[ApiController]
[Route("repo/visitors/profiles")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VisitorProfilesController : ControllerBase
{
    private readonly IVisitorRepository _visitors;

    public VisitorProfilesController(IVisitorRepository visitors) => _visitors = visitors;

    [HttpPost("identify")]
    public async Task<IActionResult> Identify([FromBody] VisitorIdentifyCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("An identity is required.");
        return VisitorResults.ToAction(this, await _visitors.IdentifyAsync(command, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        VisitorResults.ToAction(this, await _visitors.GetProfileAsync(id, ct));

    [HttpPost("by-ids")]
    public async Task<IActionResult> ByIds([FromBody] VisitorProfileIdsRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest("ids are required.");
        return VisitorResults.ToAction(this, await _visitors.GetProfilesAsync(request.Ids ?? new List<Guid>(), ct));
    }
}
