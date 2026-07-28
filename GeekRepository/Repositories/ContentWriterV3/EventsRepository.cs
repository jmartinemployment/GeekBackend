using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class ReviewCommentRepository : IReviewCommentRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ReviewCommentRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ReviewCommentDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ReviewComments.FirstOrDefaultAsync(r => r.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ReviewCommentDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default)
    {
        var entities = await _db.ReviewComments
            .Where(r => r.AssetVersionId == assetVersionId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ReviewCommentDto> CreateAsync(CreateReviewCommentCommand command, CancellationToken ct = default)
    {
        var entity = new ReviewComment
        {
            AssetVersionId = command.AssetVersionId,
            UserId = command.UserId,
            SectionPath = command.SectionPath,
            Content = command.Content,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ReviewComments.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<ReviewCommentDto> ResolveAsync(ResolveReviewCommentCommand command, CancellationToken ct = default)
    {
        var entity = await _db.ReviewComments.FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"ReviewComment {command.Id} not found");

        entity.Resolution = command.Resolution;

        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static ReviewCommentDto MapToDto(ReviewComment entity) =>
        new(entity.Id, entity.AssetVersionId, entity.UserId, entity.SectionPath,
            entity.Content, entity.Resolution, entity.CreatedAtUtc);
}

public class ApprovalEventRepository : IApprovalEventRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ApprovalEventRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ApprovalEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ApprovalEvents.FirstOrDefaultAsync(a => a.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ApprovalEventDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default)
    {
        var entities = await _db.ApprovalEvents
            .Where(a => a.AssetVersionId == assetVersionId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ApprovalEventDto> CreateAsync(CreateApprovalEventCommand command, CancellationToken ct = default)
    {
        var entity = new ApprovalEvent
        {
            AssetVersionId = command.AssetVersionId,
            UserId = command.UserId,
            Action = command.Action,
            Notes = command.Notes,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ApprovalEvents.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static ApprovalEventDto MapToDto(ApprovalEvent entity) =>
        new(entity.Id, entity.AssetVersionId, entity.UserId, entity.Action,
            entity.Notes, entity.CreatedAtUtc);
}

public class PublicationEventRepository : IPublicationEventRepository
{
    private readonly ContentWriterV3DbContext _db;

    public PublicationEventRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<PublicationEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.PublicationEvents.FirstOrDefaultAsync(p => p.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<PublicationEventDto>> GetByPublicationIdAsync(Guid publicationId, CancellationToken ct = default)
    {
        var entities = await _db.PublicationEvents
            .Where(p => p.PublicationId == publicationId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<PublicationEventDto> CreateAsync(CreatePublicationEventCommand command, CancellationToken ct = default)
    {
        var entity = new PublicationEvent
        {
            PublicationId = command.PublicationId,
            UserId = command.UserId,
            Status = command.Status,
            Details = command.Details,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PublicationEvents.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static PublicationEventDto MapToDto(PublicationEvent entity) =>
        new(entity.Id, entity.PublicationId, entity.UserId, entity.Status,
            entity.Details, entity.CreatedAtUtc);
}
