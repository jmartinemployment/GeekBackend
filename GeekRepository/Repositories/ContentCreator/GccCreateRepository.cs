using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccCreateRepository : IGccCreateRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccCreateRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccCreateDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccCreates.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccCreateDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var entities = await _db.GccCreates
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<GccCreateDto>> ListAsync(Guid? clientId, string? ownerUserId, CancellationToken ct = default)
    {
        var q = _db.GccCreates.AsQueryable();
        if (clientId is Guid cid && cid != Guid.Empty)
            q = q.Where(c => c.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(ownerUserId) && Guid.TryParse(ownerUserId, out var oid))
            q = q.Where(c => c.OwnerUserId == oid);
        var entities = await q.OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccCreateDto> CreateAsync(CreateGccCreateCommand command, CancellationToken ct = default)
    {
        var entity = new GccCreate
        {
            ClientId = command.ClientId,
            OwnerUserId = command.OwnerUserId,
            StartingContentType = command.StartingContentType,
            Topic = command.Topic,
            Notes = command.Notes,
            SiteAnalysisId = command.SiteAnalysisId,
            SiteSectionJson = command.SiteSectionJson,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.GccCreates.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<GccCreateDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var entity = await _db.GccCreates.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"GccCreate {id} not found");

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.GccCreates.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static GccCreateDto MapToDto(GccCreate entity) =>
        new(
            entity.Id,
            entity.ClientId,
            entity.OwnerUserId,
            entity.StartingContentType,
            entity.Topic,
            entity.Notes,
            entity.SiteAnalysisId,
            entity.SiteSectionJson,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
