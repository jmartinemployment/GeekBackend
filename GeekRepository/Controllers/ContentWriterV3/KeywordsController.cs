using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/keywords")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class KeywordsController : ControllerBase
{
    private readonly IKeywordCandidateRepository _repository;

    public KeywordsController(IKeywordCandidateRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KeywordCandidateDto>> GetById(Guid id, CancellationToken ct)
    {
        var keyword = await _repository.GetByIdAsync(id, ct);
        if (keyword is null)
            return NotFound();

        return Ok(keyword);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KeywordCandidateDto>>> GetByClientId([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var keywords = await _repository.GetByClientIdAsync(clientId, ct);
        return Ok(keywords);
    }

    [HttpPost]
    public async Task<ActionResult<KeywordCandidateDto>> Create([FromBody] CreateKeywordCandidateCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var keyword = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = keyword.Id }, keyword);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<KeywordCandidateDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        var keyword = await _repository.UpdateStatusAsync(id, request.Status, ct);
        return Ok(keyword);
    }

    public record UpdateStatusRequest(string Status);
}
