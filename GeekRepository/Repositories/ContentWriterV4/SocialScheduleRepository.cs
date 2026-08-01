using GeekApplication.Interfaces.ContentWriterV4;
using GeekApplication.Models.ContentWriterV4;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV4;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV4;

public class SocialScheduleRepository : ISocialScheduleRepository
{
    private readonly ContentWriterV4DbContext _db;

    public SocialScheduleRepository(ContentWriterV4DbContext db) => _db = db;

    public async Task<SocialScheduleEntryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.SocialScheduleEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<SocialScheduleEntryDto>> GetByOwnerIdAsync(
        Guid ownerId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        Guid? campaignId = null,
        CancellationToken ct = default)
    {
        var q = _db.SocialScheduleEntries.AsQueryable().Where(e => e.OwnerId == ownerId);
        if (campaignId is Guid cid && cid != Guid.Empty)
            q = q.Where(e => e.CampaignId == cid);
        if (fromUtc is DateTime from)
            q = q.Where(e => e.ScheduledAtUtc >= from);
        if (toUtc is DateTime to)
            q = q.Where(e => e.ScheduledAtUtc <= to);

        var entities = await q.OrderBy(e => e.ScheduledAtUtc).ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<SocialScheduleEntryDto>> GetByCampaignIdAsync(
        Guid campaignId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        var q = _db.SocialScheduleEntries.AsQueryable().Where(e => e.CampaignId == campaignId);
        if (fromUtc is DateTime from)
            q = q.Where(e => e.ScheduledAtUtc >= from);
        if (toUtc is DateTime to)
            q = q.Where(e => e.ScheduledAtUtc <= to);

        var entities = await q.OrderBy(e => e.ScheduledAtUtc).ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<SocialScheduleEntryDto> CreateAsync(
        CreateSocialScheduleEntryCommand command,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entity = new SocialScheduleEntry
        {
            OwnerId = command.OwnerId,
            CampaignId = command.CampaignId,
            AssetId = command.AssetId,
            AssetVersionId = command.AssetVersionId,
            Channel = command.Channel,
            ScheduledAtUtc = DateTime.SpecifyKind(command.ScheduledAtUtc, DateTimeKind.Utc),
            Status = "scheduled",
            Title = command.Title,
            Notes = command.Notes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.SocialScheduleEntries.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<SocialScheduleEntryDto> UpdateAsync(
        UpdateSocialScheduleEntryCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.SocialScheduleEntries.FirstOrDefaultAsync(e => e.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"SocialScheduleEntry {command.Id} not found");

        entity.Channel = command.Channel;
        entity.ScheduledAtUtc = DateTime.SpecifyKind(command.ScheduledAtUtc, DateTimeKind.Utc);
        entity.Status = command.Status;
        entity.Title = command.Title;
        entity.Notes = command.Notes;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        _db.SocialScheduleEntries.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.SocialScheduleEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            return false;
        _db.SocialScheduleEntries.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static SocialScheduleEntryDto MapToDto(SocialScheduleEntry entity) =>
        new(
            entity.Id,
            entity.OwnerId,
            entity.CampaignId,
            entity.AssetId,
            entity.AssetVersionId,
            entity.Channel,
            entity.ScheduledAtUtc,
            entity.Status,
            entity.Title,
            entity.Notes,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
