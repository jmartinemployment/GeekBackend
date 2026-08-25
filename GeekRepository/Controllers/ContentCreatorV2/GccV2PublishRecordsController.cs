using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Audit trail of CMS publish attempts (see <see cref="GccV2PublishRecord"/>). This controller only
/// persists/serves the record rows — GeekAPI's <c>GccV2CmsPublishService</c> is the only caller that
/// talks to the Geek blog CMS itself, via <c>IBlogRepository</c>.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/publish-records")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2PublishRecordsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2PublishRecordsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2PublishRecord>> GetById(Guid id, CancellationToken ct)
    {
        var record = await _db.GccV2PublishRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return record is null ? NotFound() : Ok(record);
    }

    /// <summary>Latest-first list of publish attempts for a create.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2PublishRecord>>> List(
        [FromQuery] Guid createId,
        CancellationToken ct)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var results = await _db.GccV2PublishRecords.AsNoTracking()
            .Where(r => r.CreateId == createId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2PublishRecord>> Create([FromBody] CreateGccV2PublishRecordCommand command, CancellationToken ct)
    {
        if (command is null || command.CreateId == Guid.Empty || command.JobId == Guid.Empty || string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("createId, jobId, and ownerUserId are required");

        var record = new GccV2PublishRecord
        {
            Id = Guid.NewGuid(),
            CreateId = command.CreateId,
            JobId = command.JobId,
            OwnerUserId = command.OwnerUserId,
            Channel = string.IsNullOrWhiteSpace(command.Channel) ? "blog" : command.Channel.Trim().ToLowerInvariant(),
            Status = string.IsNullOrWhiteSpace(command.Status) ? "draft" : command.Status.Trim().ToLowerInvariant(),
            ExternalPostId = command.ExternalPostId,
            Slug = command.Slug ?? string.Empty,
            PublicUrl = command.PublicUrl,
            Title = command.Title ?? string.Empty,
            MetaDescription = command.MetaDescription,
            Error = command.Error,
            BodyDocumentJson = command.BodyDocumentJson,
            IsPublished = command.IsPublished ?? false,
            PublishedAtUtc = command.PublishedAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.GccV2PublishRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2PublishRecord>> Patch(Guid id, [FromBody] PatchGccV2PublishRecordCommand command, CancellationToken ct)
    {
        var record = await _db.GccV2PublishRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null) return NotFound();

        if (command.Status is not null) record.Status = command.Status.Trim().ToLowerInvariant();
        if (command.ExternalPostId is not null) record.ExternalPostId = command.ExternalPostId;
        if (command.Slug is not null) record.Slug = command.Slug;
        if (command.PublicUrl is not null) record.PublicUrl = command.PublicUrl;
        if (command.Error is not null) record.Error = command.Error;
        if (command.IsPublished is not null) record.IsPublished = command.IsPublished.Value;
        if (command.PublishedAtUtc is not null) record.PublishedAtUtc = command.PublishedAtUtc;

        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(record);
    }

    public sealed record CreateGccV2PublishRecordCommand(
        Guid CreateId,
        Guid JobId,
        string OwnerUserId,
        string? Channel,
        string? Status,
        int? ExternalPostId,
        string? Slug,
        string? PublicUrl,
        string? Title,
        string? MetaDescription,
        string? Error,
        string? BodyDocumentJson,
        bool? IsPublished,
        DateTimeOffset? PublishedAtUtc);

    public sealed record PatchGccV2PublishRecordCommand(
        string? Status = null,
        int? ExternalPostId = null,
        string? Slug = null,
        string? PublicUrl = null,
        string? Error = null,
        bool? IsPublished = null,
        DateTimeOffset? PublishedAtUtc = null);
}
