using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccApprovalEventRepository : IGccApprovalEventRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccApprovalEventRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccApprovalEventDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccApprovalEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccApprovalEventDto>> GetByArtifactVersionIdAsync(Guid artifactVersionId, CancellationToken ct = default)
    {
        var entities = await _db.GccApprovalEvents
            .Where(e => e.ArtifactVersionId == artifactVersionId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccApprovalEventDto> CreateAsync(CreateGccApprovalEventCommand command, CancellationToken ct = default)
    {
        var entity = new GccApprovalEvent
        {
            ArtifactVersionId = command.ArtifactVersionId,
            UserId = command.UserId,
            Action = command.Action,
            Notes = command.Notes,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.GccApprovalEvents.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static GccApprovalEventDto MapToDto(GccApprovalEvent entity) =>
        new(
            entity.Id,
            entity.ArtifactVersionId,
            entity.UserId,
            entity.Action,
            entity.Notes,
            entity.CreatedAtUtc);
}
