using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/keywords")]
public class KeywordsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<KeywordsController> _logger;

    public KeywordsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<KeywordsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KeywordCandidateDto>> GetById(Guid id, CancellationToken ct)
    {
        var keyword = await _repo.GetKeywordByIdAsync(id, ct);
        if (keyword is null)
            return NotFound();

        return Ok(keyword);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KeywordCandidateDto>>> GetByClientId(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        _logger.LogInformation("User {UserId} fetching keywords for client {ClientId}",
            _currentUser.UserId, clientId);

        var keywords = await _repo.GetKeywordsByClientIdAsync(clientId, ct);
        return Ok(keywords);
    }

    [HttpPost]
    public async Task<ActionResult<KeywordCandidateDto>> Create([FromBody] CreateKeywordCandidateCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating keyword for client {ClientId}",
            _currentUser.UserId, command.ClientId);

        var keyword = await _repo.CreateKeywordAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = keyword.Id }, keyword);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<KeywordCandidateDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} updating keyword {KeywordId} status to {Status}",
            _currentUser.UserId, id, request.Status);

        var keyword = await _repo.UpdateKeywordStatusAsync(id, request.Status, ct);
        return Ok(keyword);
    }

    public record UpdateStatusRequest(string Status);
}
