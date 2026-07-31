using GeekApplication.Interfaces.ContentWriterV4;
using GeekApplication.Models.ContentWriterV4;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV4;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV4;

public class BrandVoiceRepository : IBrandVoiceRepository
{
    private readonly ContentWriterV4DbContext _db;

    public BrandVoiceRepository(ContentWriterV4DbContext db) => _db = db;

    public async Task<BrandVoiceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.BrandVoices.FirstOrDefaultAsync(b => b.Id == id, ct);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<BrandVoiceDto>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default)
    {
        var entities = await _db.BrandVoices
            .Where(b => b.OwnerId == ownerId)
            .OrderByDescending(b => b.UpdatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<BrandVoiceDto> CreateAsync(CreateBrandVoiceCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entity = new BrandVoice
        {
            OwnerId = command.OwnerId,
            Name = command.Name,
            Description = command.Description,
            Tone = command.Tone,
            SampleText = command.SampleText,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.BrandVoices.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<BrandVoiceDto> UpdateAsync(UpdateBrandVoiceCommand command, CancellationToken ct = default)
    {
        var entity = await _db.BrandVoices.FirstOrDefaultAsync(b => b.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"BrandVoice {command.Id} not found");

        entity.Name = command.Name;
        entity.Description = command.Description;
        entity.Tone = command.Tone;
        entity.SampleText = command.SampleText;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        _db.BrandVoices.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.BrandVoices.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null)
            return false;
        _db.BrandVoices.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static BrandVoiceDto MapToDto(BrandVoice entity) =>
        new(
            entity.Id,
            entity.OwnerId,
            entity.Name,
            entity.Description,
            entity.Tone,
            entity.SampleText,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
