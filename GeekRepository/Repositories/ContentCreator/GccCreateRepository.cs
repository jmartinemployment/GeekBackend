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

    /// <summary>
    /// Delete a create and everything beneath it, in foreign-key order.
    ///
    /// The cascade is written out because nothing else performs it: GccArtifact.CreateId,
    /// GccArtifactVersion.ArtifactId and GccApprovalEvent.ArtifactVersionId are bare Guid columns
    /// with no navigation property and no OnDelete configured, so EF deletes exactly what it is
    /// told and no more. Deleting the create row alone would leave its artifacts and versions
    /// behind, reachable by nothing.
    ///
    /// One SaveChangesAsync, so the whole graph goes or none of it does.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var create = await _db.GccCreates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (create is null) return false;

        var artifactIds = await _db.GccArtifacts
            .Where(a => a.CreateId == id)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var versionIds = await _db.GccArtifactVersions
            .Where(v => artifactIds.Contains(v.ArtifactId))
            .Select(v => v.Id)
            .ToListAsync(ct);

        var approvalEvents = await _db.GccApprovalEvents
            .Where(e => versionIds.Contains(e.ArtifactVersionId))
            .ToListAsync(ct);
        _db.GccApprovalEvents.RemoveRange(approvalEvents);

        var versions = await _db.GccArtifactVersions
            .Where(v => versionIds.Contains(v.Id))
            .ToListAsync(ct);
        _db.GccArtifactVersions.RemoveRange(versions);

        var artifacts = await _db.GccArtifacts
            .Where(a => artifactIds.Contains(a.Id))
            .ToListAsync(ct);
        _db.GccArtifacts.RemoveRange(artifacts);

        _db.GccCreates.Remove(create);

        await _db.SaveChangesAsync(ct);
        return true;
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
            ProjectId = command.ProjectId,
            OwnerUserId = command.OwnerUserId,
            StartingContentType = command.StartingContentType,
            Topic = command.Topic,
            Notes = command.Notes,
            Department = string.IsNullOrWhiteSpace(command.Department) ? "marketing" : command.Department.Trim(),
            ProjectSiteRunId = command.ProjectSiteRunId,
            SiteSectionJson = command.SiteSectionJson,
            BriefJson = command.BriefJson,
            ResearchJson = command.ResearchJson,
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

    public async Task<GccCreateDto> UpdateBriefResearchAsync(
        Guid id,
        UpdateGccCreateBriefResearchCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccCreates.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"GccCreate {id} not found");

        if (command.BriefJson is not null)
            entity.BriefJson = string.IsNullOrWhiteSpace(command.BriefJson) ? null : command.BriefJson;
        if (command.ResearchJson is not null)
            entity.ResearchJson = string.IsNullOrWhiteSpace(command.ResearchJson) ? null : command.ResearchJson;
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
            entity.ProjectSiteRunId,
            entity.SiteSectionJson,
            entity.BriefJson,
            entity.ResearchJson,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            string.IsNullOrWhiteSpace(entity.Department) ? "marketing" : entity.Department,
            entity.ProjectId);
}
