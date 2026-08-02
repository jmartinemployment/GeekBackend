using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/site-analyses")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccSiteAnalysesController : ControllerBase
{
    private readonly IGccSiteAnalysisRepository _repo;
    public GccSiteAnalysesController(IGccSiteAnalysisRepository repo) => _repo = repo;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccSiteAnalysisDto>> GetById(Guid id, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<GccSiteAnalysisDto>> Create(
        [FromBody] CreateGccSiteAnalysisCommand command,
        CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Domain))
            return BadRequest("domain required");
        var created = await _repo.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccSiteAnalysisDto>> Update(
        Guid id,
        [FromBody] UpdateGccSiteAnalysisCommand command,
        CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Status))
            return BadRequest("status required");

        var updated = await _repo.UpdateAsync(id, command, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{id:guid}/findings")]
    public async Task<ActionResult<IReadOnlyList<GccSiteFindingDto>>> ListFindings(Guid id, CancellationToken ct)
    {
        if (await _repo.GetByIdAsync(id, ct) is null)
            return NotFound();

        return Ok(await _repo.ListByAnalysisIdAsync(id, ct));
    }

    [HttpPut("{id:guid}/findings")]
    [HttpPost("{id:guid}/findings")]
    public async Task<ActionResult<IReadOnlyList<GccSiteFindingDto>>> ReplaceFindings(
        Guid id,
        [FromBody] CreateGccSiteFindingsCommand command,
        CancellationToken ct)
    {
        if (command?.Findings is null)
            return BadRequest("findings required");
        if (command.Findings.Any(f =>
                string.IsNullOrWhiteSpace(f.FindingType)
                || string.IsNullOrWhiteSpace(f.Severity)
                || string.IsNullOrWhiteSpace(f.Title)
                || string.IsNullOrWhiteSpace(f.Summary)))
        {
            return BadRequest("findingType, severity, title, and summary are required for every finding");
        }
        if (await _repo.GetByIdAsync(id, ct) is null)
            return NotFound();

        return Ok(await _repo.ReplaceFindingsAsync(id, command, ct));
    }
}
