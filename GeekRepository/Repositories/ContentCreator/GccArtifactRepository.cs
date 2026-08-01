using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccArtifactRepository : IGccArtifactRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccArtifactRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccArtifactDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccArtifacts.FirstOrDefaultAsync(a => a.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccArtifactDto>> GetByCreateIdAsync(Guid createId, CancellationToken ct = default)
    {
        var entities = await _db.GccArtifacts
            .Where(a => a.CreateId == createId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccArtifactDto> CreateAsync(CreateGccArtifactCommand command, CancellationToken ct = default)
    {
        var entity = new GccArtifact
        {
            CreateId = command.CreateId,
            ParentArtifactId = command.ParentArtifactId,
            Type = command.Type,
            Name = command.Name,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.GccArtifacts.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<GccArtifactDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var entity = await _db.GccArtifacts.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"GccArtifact {id} not found");

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.GccArtifacts.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static GccArtifactDto MapToDto(GccArtifact entity) =>
        new(
            entity.Id,
            entity.CreateId,
            entity.ParentArtifactId,
            entity.Type,
            entity.Name,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
