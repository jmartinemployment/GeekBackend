using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class ContentCampaignRepository : IContentCampaignRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ContentCampaignRepository(ContentWriterV3DbContext db)
    {
        _db = db;
    }

    public async Task<ContentCampaignDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ContentCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ContentCampaignDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var entities = await _db.ContentCampaigns
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ContentCampaignDto> CreateAsync(CreateContentCampaignCommand command, CancellationToken ct = default)
    {
        var entity = new ContentCampaign
        {
            ClientId = command.ClientId,
            Name = command.Name,
            Keyword = command.Keyword,
            ProfileVersionId = command.ProfileVersionId,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ContentCampaigns.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<ContentCampaignDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var entity = await _db.ContentCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Campaign {id} not found");

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.ContentCampaigns.Update(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ContentCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity == null)
            return false;

        _db.ContentCampaigns.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ContentCampaignDto MapToDto(ContentCampaign entity) =>
        new(
            entity.Id,
            entity.ClientId,
            entity.Name,
            entity.Keyword,
            entity.Status,
            entity.ProfileVersionId,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.RowVersion);
}
