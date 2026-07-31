using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class ContentAssetRepository : IContentAssetRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ContentAssetRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ContentAssetDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ContentAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ContentAssetDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        var entities = await _db.ContentAssets
            .Where(a => a.CampaignId == campaignId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ContentAssetDto> CreateAsync(CreateContentAssetCommand command, CancellationToken ct = default)
    {
        var entity = new ContentAsset
        {
            CampaignId = command.CampaignId,
            Type = command.Type,
            Name = command.Name,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ContentAssets.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ContentAssetDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var entity = await _db.ContentAssets.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"ContentAsset {id} not found");

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        _db.ContentAssets.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ContentAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity == null)
            return false;

        _db.ContentAssets.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ContentAssetDto MapToDto(ContentAsset entity) =>
        new(entity.Id, entity.CampaignId, entity.Type, entity.Name, entity.Status,
            entity.CreatedAtUtc, entity.UpdatedAtUtc);
}

public class ContentAssetVersionRepository : IContentAssetVersionRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ContentAssetVersionRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ContentAssetVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ContentAssetVersions.FirstOrDefaultAsync(v => v.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ContentAssetVersionDto>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        var entities = await _db.ContentAssetVersions
            .Where(v => v.AssetId == assetId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ContentAssetVersionDto> CreateAsync(CreateContentAssetVersionCommand command, CancellationToken ct = default)
    {
        // Get the next version number
        var maxVersion = await _db.ContentAssetVersions
            .Where(v => v.AssetId == command.AssetId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var entity = new ContentAssetVersion
        {
            AssetId = command.AssetId,
            VersionNumber = maxVersion + 1,
            BodyDocumentJson = command.BodyDocumentJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ContentAssetVersions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ContentAssetVersionDto> UpdateAsync(UpdateContentAssetVersionCommand command, CancellationToken ct = default)
    {
        var entity = await _db.ContentAssetVersions.FirstOrDefaultAsync(v => v.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"ContentAssetVersion {command.Id} not found");

        entity.BodyDocumentJson = command.BodyDocumentJson;

        _db.ContentAssetVersions.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ContentAssetVersionDto MapToDto(ContentAssetVersion entity) =>
        new(entity.Id, entity.AssetId, entity.VersionNumber, entity.BodyDocumentJson,
            entity.RowVersion, entity.CreatedAtUtc);
}
