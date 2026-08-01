using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccArtifactVersionRepository : IGccArtifactVersionRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccArtifactVersionRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccArtifactVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccArtifactVersions.FirstOrDefaultAsync(v => v.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccArtifactVersionDto>> GetByArtifactIdAsync(Guid artifactId, CancellationToken ct = default)
    {
        var entities = await _db.GccArtifactVersions
            .Where(v => v.ArtifactId == artifactId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccArtifactVersionDto> CreateAsync(CreateGccArtifactVersionCommand command, CancellationToken ct = default)
    {
        var maxVersion = await _db.GccArtifactVersions
            .Where(v => v.ArtifactId == command.ArtifactId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var entity = new GccArtifactVersion
        {
            ArtifactId = command.ArtifactId,
            VersionNumber = maxVersion + 1,
            BodyJson = command.BodyDocumentJson,
            MetadataJson = command.MetadataJson,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.GccArtifactVersions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static GccArtifactVersionDto MapToDto(GccArtifactVersion entity) =>
        new(
            entity.Id,
            entity.ArtifactId,
            entity.VersionNumber,
            entity.BodyJson,
            entity.MetadataJson,
            entity.RowVersion,
            entity.CreatedAtUtc);
}
