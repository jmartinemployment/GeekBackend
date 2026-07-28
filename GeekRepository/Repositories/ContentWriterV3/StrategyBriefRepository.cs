using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class StrategyBriefRepository : IStrategyBriefRepository
{
    private readonly ContentWriterV3DbContext _db;

    public StrategyBriefRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<StrategyBriefDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.StrategyBriefs.FirstOrDefaultAsync(s => s.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<StrategyBriefDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        var entities = await _db.StrategyBriefs
            .Where(s => s.CampaignId == campaignId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<StrategyBriefDto> CreateAsync(CreateStrategyBriefCommand command, CancellationToken ct = default)
    {
        var entity = new StrategyBrief
        {
            CampaignId = command.CampaignId,
            PainPointId = command.PainPointId,
            AudienceProfile = command.AudienceProfile,
            BuyingStage = command.BuyingStage,
            Angle = command.Angle,
            CallToAction = command.CallToAction,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.StrategyBriefs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<StrategyBriefDto> UpdateAsync(UpdateStrategyBriefCommand command, CancellationToken ct = default)
    {
        var entity = await _db.StrategyBriefs.FirstOrDefaultAsync(s => s.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"StrategyBrief {command.Id} not found");

        entity.AudienceProfile = command.AudienceProfile;
        entity.BuyingStage = command.BuyingStage;
        entity.Angle = command.Angle;
        entity.CallToAction = command.CallToAction;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.StrategyBriefs.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<StrategyBriefDto> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.StrategyBriefs.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException($"StrategyBrief {id} not found");

        entity.Status = "approved";
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.StrategyBriefs.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<StrategyBriefDto> RejectAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.StrategyBriefs.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException($"StrategyBrief {id} not found");

        entity.Status = "rejected";
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.StrategyBriefs.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static StrategyBriefDto MapToDto(StrategyBrief entity) =>
        new(entity.Id, entity.CampaignId, entity.PainPointId, entity.AudienceProfile,
            entity.BuyingStage, entity.Angle, entity.CallToAction, entity.Status,
            entity.RowVersion, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
