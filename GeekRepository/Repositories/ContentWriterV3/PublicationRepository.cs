using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class PublicationRepository : IPublicationRepository
{
    private readonly ContentWriterV3DbContext _db;

    public PublicationRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<PublicationDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Publications.FirstOrDefaultAsync(p => p.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<PublicationDto>> GetByAssetVersionIdAsync(Guid assetVersionId, CancellationToken ct = default)
    {
        var entities = await _db.Publications
            .Where(p => p.AssetVersionId == assetVersionId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<PublicationDto> CreateAsync(CreatePublicationCommand command, CancellationToken ct = default)
    {
        var entity = new Publication
        {
            AssetVersionId = command.AssetVersionId,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Publications.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<PublicationDto> UpdateStatusAsync(UpdatePublicationStatusCommand command, CancellationToken ct = default)
    {
        var entity = await _db.Publications.FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Publication {command.Id} not found");

        entity.Status = command.Status;
        entity.ResponseSnapshot = command.ResponseSnapshot;
        entity.Error = command.Error;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.Publications.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static PublicationDto MapToDto(Publication entity) =>
        new(entity.Id, entity.AssetVersionId, entity.Status, entity.ResponseSnapshot,
            entity.Error, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
