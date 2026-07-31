using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/pain-points")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class PainPointsController : ControllerBase
{
    private readonly IPainPointRepository _repository;

    public PainPointsController(IPainPointRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PainPointDto>> GetById(Guid id, CancellationToken ct)
    {
        var painPoint = await _repository.GetByIdAsync(id, ct);
        if (painPoint is null)
            return NotFound();

        return Ok(painPoint);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PainPointDto>>> GetByClientId([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var painPoints = await _repository.GetByClientIdAsync(clientId, ct);
        return Ok(painPoints);
    }

    [HttpPost]
    public async Task<ActionResult<PainPointDto>> Create([FromBody] CreatePainPointCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var painPoint = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = painPoint.Id }, painPoint);
    }
}

[ApiController]
[Route("repo/content-writer-v3/strategy-briefs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class StrategyBriefsController : ControllerBase
{
    private readonly IStrategyBriefRepository _repository;

    public StrategyBriefsController(IStrategyBriefRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StrategyBriefDto>> GetById(Guid id, CancellationToken ct)
    {
        var brief = await _repository.GetByIdAsync(id, ct);
        if (brief is null)
            return NotFound();

        return Ok(brief);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StrategyBriefDto>>> GetByCampaignId([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var briefs = await _repository.GetByCampaignIdAsync(campaignId, ct);
        return Ok(briefs);
    }

    [HttpPost]
    public async Task<ActionResult<StrategyBriefDto>> Create([FromBody] CreateStrategyBriefCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var brief = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = brief.Id }, brief);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StrategyBriefDto>> Update(Guid id, [FromBody] UpdateStrategyBriefCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var updated = command with { Id = id };
        try
        {
            var brief = await _repository.UpdateAsync(updated, ct);
            return Ok(brief);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<StrategyBriefDto>> Approve(Guid id, CancellationToken ct)
    {
        var brief = await _repository.ApproveAsync(id, ct);
        return Ok(brief);
    }

    [HttpPatch("{id:guid}/reject")]
    public async Task<ActionResult<StrategyBriefDto>> Reject(Guid id, CancellationToken ct)
    {
        var brief = await _repository.RejectAsync(id, ct);
        return Ok(brief);
    }
}

[ApiController]
[Route("repo/content-writer-v3/publications")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class PublicationsController : ControllerBase
{
    private readonly IPublicationRepository _repository;

    public PublicationsController(IPublicationRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationDto>> GetById(Guid id, CancellationToken ct)
    {
        var publication = await _repository.GetByIdAsync(id, ct);
        if (publication is null)
            return NotFound();

        return Ok(publication);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationDto>>> GetByAssetVersionId(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var publications = await _repository.GetByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(publications);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationDto>> Create([FromBody] CreatePublicationCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var publication = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = publication.Id }, publication);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<PublicationDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        var publication = await _repository.UpdateStatusAsync(
            new UpdatePublicationStatusCommand(id, request.Status, null, null), ct);
        return Ok(publication);
    }

    public record UpdateStatusRequest(string Status);
}
