using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class PainPointRepository : IPainPointRepository
{
    private readonly ContentWriterV3DbContext _db;

    public PainPointRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<PainPointDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.PainPoints.FirstOrDefaultAsync(p => p.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<PainPointDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var entities = await _db.PainPoints
            .Where(p => p.ClientId == clientId && p.StaleeSinceUtc == null)
            .OrderByDescending(p => p.Confidence)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<PainPointDto> CreateAsync(CreatePainPointCommand command, CancellationToken ct = default)
    {
        var entity = new PainPoint
        {
            ClientId = command.ClientId,
            Name = command.Name,
            Description = command.Description,
            ReaderSymptom = command.ReaderSymptom,
            CostOfInaction = command.CostOfInaction,
            OfferTerminology = command.OfferTerminology,
            Objections = command.Objections,
            Confidence = command.Confidence,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.PainPoints.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<PainPointDto> UpdateAsync(UpdatePainPointCommand command, CancellationToken ct = default)
    {
        var entity = await _db.PainPoints.FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"PainPoint {command.Id} not found");

        entity.Name = command.Name;
        entity.Description = command.Description;
        entity.ReaderSymptom = command.ReaderSymptom;
        entity.CostOfInaction = command.CostOfInaction;
        entity.OfferTerminology = command.OfferTerminology;
        entity.Objections = command.Objections;
        entity.Confidence = command.Confidence;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.PainPoints.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<PainPointDto> MarkStaleAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.PainPoints.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"PainPoint {id} not found");

        entity.StaleeSinceUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.PainPoints.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static PainPointDto MapToDto(PainPoint entity) =>
        new(entity.Id, entity.ClientId, entity.Name, entity.Description,
            entity.ReaderSymptom, entity.CostOfInaction, entity.OfferTerminology,
            entity.Objections, entity.Confidence, entity.StaleeSinceUtc,
            entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
