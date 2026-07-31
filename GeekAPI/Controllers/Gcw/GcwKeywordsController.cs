using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing keyword candidate API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/keywords")]
public class GcwKeywordsController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "research-queued",
        "researched",
        "briefed",
        "rejected",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwKeywordsController> _logger;

    public GcwKeywordsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwKeywordsController> logger)
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
    public async Task<ActionResult<IReadOnlyList<KeywordCandidateDto>>> List(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var keywords = await _repo.GetKeywordsByClientIdAsync(clientId, ct);
        return Ok(keywords);
    }

    [HttpPost]
    public async Task<ActionResult<KeywordCandidateDto>> Create(
        [FromBody] CreateGcwKeywordRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return BadRequest("keyword is required");

        _logger.LogInformation(
            "GCW user {UserId} creating keyword for client {ClientId}",
            _currentUser.UserId,
            request.ClientId);

        var keyword = await _repo.CreateKeywordAsync(
            new CreateKeywordCandidateCommand(
                request.ClientId,
                request.Keyword.Trim(),
                request.SearchVolume,
                request.Difficulty,
                string.IsNullOrWhiteSpace(request.Intent) ? null : request.Intent.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = keyword.Id }, keyword);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<KeywordCandidateDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwKeywordStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        var status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");

        _logger.LogInformation(
            "GCW user {UserId} updating keyword {KeywordId} status to {Status}",
            _currentUser.UserId,
            id,
            status);

        var keyword = await _repo.UpdateKeywordStatusAsync(id, status, ct);
        return Ok(keyword);
    }

    public sealed record CreateGcwKeywordRequest(
        Guid ClientId,
        string Keyword,
        int? SearchVolume = null,
        int? Difficulty = null,
        string? Intent = null);

    public sealed record UpdateGcwKeywordStatusRequest(string Status);
}
