using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentWriterV2;

/// <summary>
/// Cross-project tool-content cache for content-writer-v2's ToolPageGenerator — lets the same
/// real-world product (e.g. "Zapier") reuse its shared overview/capabilities content across
/// departments instead of regenerating it from scratch every time. Keyed on a normalized name, a
/// real queryable column — unlike BlobsController's generic (collection, id) store, which has no
/// way to look up "have I seen this tool before" at all.
/// </summary>
[ApiController]
[Route("repo/content-writer-v2/tool-content-cache")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ToolContentCacheController : ControllerBase
{
    private readonly ContentWriterV2DbContext _db;

    public ToolContentCacheController(ContentWriterV2DbContext db) => _db = db;

    public record ToolContentCacheDto(string NormalizedToolName, string DisplayName, string OverviewJson, DateTime UpdatedAtUtc);

    [HttpGet("{normalizedToolName}")]
    public async Task<ActionResult<ToolContentCacheDto>> Get(string normalizedToolName, CancellationToken ct)
    {
        var entry = await _db.ToolContentCaches
            .FirstOrDefaultAsync(t => t.NormalizedToolName == normalizedToolName, ct);

        if (entry is null)
        {
            return NotFound();
        }

        return Ok(new ToolContentCacheDto(entry.NormalizedToolName, entry.DisplayName, entry.OverviewJson, entry.UpdatedAtUtc));
    }

    public record SaveToolContentCacheRequest(string DisplayName, string OverviewJson);

    [HttpPut("{normalizedToolName}")]
    public async Task<ActionResult> Save(string normalizedToolName, [FromBody] SaveToolContentCacheRequest request, CancellationToken ct)
    {
        var entry = await _db.ToolContentCaches
            .FirstOrDefaultAsync(t => t.NormalizedToolName == normalizedToolName, ct);

        if (entry is null)
        {
            _db.ToolContentCaches.Add(new ToolContentCache
            {
                NormalizedToolName = normalizedToolName,
                DisplayName = request.DisplayName,
                OverviewJson = request.OverviewJson,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            entry.DisplayName = request.DisplayName;
            entry.OverviewJson = request.OverviewJson;
            entry.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
