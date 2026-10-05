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
        return entities.Select(a => MapToDto(a)).ToList().AsReadOnly();
    }

    /// <remarks>
    /// Drafts are keyed by create until they are re-keyed to the project, so the project's drafts are
    /// the drafts of its creates. One query over the join, not a read per create.
    /// </remarks>
    /// <remarks>
    /// Each draft carries its newest version's number and time. A Generate rewrites a page as a new
    /// version, so when a page was first created stops being when its text was written; the page
    /// lists drafts by the second, and reading it here is one grouped query rather than a versions
    /// read per draft from the browser. Ordered by that time, most recently written first.
    /// </remarks>
    public async Task<IReadOnlyList<GccArtifactDto>> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var entities = await _db.GccArtifacts
            .Where(a => _db.GccCreates.Any(c => c.Id == a.CreateId && c.ProjectId == projectId))
            .ToListAsync(ct);
        var ids = entities.Select(a => a.Id).ToList();
        var latest = (await _db.GccArtifactVersions
                .Where(v => ids.Contains(v.ArtifactId))
                .GroupBy(v => v.ArtifactId)
                .Select(g => new { ArtifactId = g.Key, Number = g.Max(v => v.VersionNumber), At = g.Max(v => v.CreatedAtUtc) })
                .ToListAsync(ct))
            .ToDictionary(v => v.ArtifactId);

        return entities
            .Select(a => latest.TryGetValue(a.Id, out var v) ? MapToDto(a, v.Number, v.At) : MapToDto(a))
            .OrderByDescending(d => d.LatestVersionAtUtc ?? d.CreatedAtUtc)
            .ThenByDescending(d => d.CreatedAtUtc)
            .ToList()
            .AsReadOnly();
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

    internal static GccArtifactDto MapToDto(
        GccArtifact entity, int? latestVersionNumber = null, DateTime? latestVersionAtUtc = null) =>
        new(
            entity.Id,
            entity.CreateId,
            entity.ParentArtifactId,
            entity.Type,
            entity.Name,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            latestVersionNumber,
            latestVersionAtUtc);
}
