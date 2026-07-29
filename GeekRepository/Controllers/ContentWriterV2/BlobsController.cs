using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentWriterV2;

/// <summary>
/// Generic JSON-blob persistence for the separate .NET content-writer-v2 product's own
/// IPersistenceStore. Collection is an arbitrary caller-chosen name — no schema change needed
/// to add a new one.
/// </summary>
[ApiController]
[Route("repo/content-writer-v2/blobs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class BlobsController : ControllerBase
{
    private readonly ContentWriterV2DbContext _db;

    public BlobsController(ContentWriterV2DbContext db) => _db = db;

    [HttpGet("{collection}")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> ListIds(string collection, CancellationToken ct)
    {
        var ids = await _db.Blobs
            .Where(b => b.Collection == collection)
            .Select(b => b.ItemId)
            .ToListAsync(ct);

        return Ok(ids);
    }

    [HttpGet("{collection}/{id:guid}")]
    public async Task<ActionResult<string>> Get(string collection, Guid id, CancellationToken ct)
    {
        var blob = await _db.Blobs.FirstOrDefaultAsync(b => b.Collection == collection && b.ItemId == id, ct);
        if (blob is null)
            return NotFound();

        return Content(blob.DataJson, "application/json");
    }

    [HttpPut("{collection}/{id:guid}")]
    public async Task<ActionResult> Save(string collection, Guid id, [FromBody] object data, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var blob = await _db.Blobs.FirstOrDefaultAsync(b => b.Collection == collection && b.ItemId == id, ct);

        if (blob is null)
        {
            _db.Blobs.Add(new Blob
            {
                Collection = collection,
                ItemId = id,
                DataJson = json,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            blob.DataJson = json;
            blob.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{collection}/{id:guid}")]
    public async Task<ActionResult> Delete(string collection, Guid id, CancellationToken ct)
    {
        var blob = await _db.Blobs.FirstOrDefaultAsync(b => b.Collection == collection && b.ItemId == id, ct);
        if (blob is null)
            return NotFound();

        _db.Blobs.Remove(blob);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
