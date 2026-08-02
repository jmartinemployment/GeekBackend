using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/creates")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccCreatesController : ControllerBase
{
    private readonly IGccCreateRepository _repository;
    private readonly ILogger<GccCreatesController> _logger;

    public GccCreatesController(IGccCreateRepository repository, ILogger<GccCreatesController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccCreateDto>> GetById(Guid id, CancellationToken ct)
    {
        var create = await _repository.GetByIdAsync(id, ct);
        if (create is null)
            return NotFound();

        return Ok(create);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccCreateDto>>> List(
        [FromQuery] Guid? clientId,
        [FromQuery] string? ownerUserId,
        CancellationToken ct)
    {
        if (clientId is Guid cid && cid != Guid.Empty && string.IsNullOrWhiteSpace(ownerUserId))
            return Ok(await _repository.GetByClientIdAsync(cid, ct));

        return Ok(await _repository.ListAsync(clientId, ownerUserId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccCreateDto>> Create([FromBody] CreateGccCreateCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var create = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = create.Id }, create);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<GccCreateDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        try
        {
            var create = await _repository.UpdateStatusAsync(id, request.Status, ct);
            return Ok(create);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/brief-research")]
    public async Task<ActionResult<GccCreateDto>> UpdateBriefResearch(
        Guid id,
        [FromBody] UpdateGccCreateBriefResearchCommand request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body required");
        if (request.BriefJson is null && request.ResearchJson is null)
            return BadRequest("briefJson and/or researchJson required");

        try
        {
            var create = await _repository.UpdateBriefResearchAsync(id, request, ct);
            return Ok(create);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public record UpdateStatusRequest(string Status);
}
