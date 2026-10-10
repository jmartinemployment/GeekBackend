using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// A page's content. A page has one version -- its current text -- and writing new text replaces it.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-06: "no history is required ... the old created content should be deleted". Until then
/// every write added a version beside the last, and the page showed "v3" over a
/// history nobody read. Now the old text goes, with everything that hung off it: its evidence (what the
/// writer was given to produce text that no longer exists) and its approval events (an approval of text
/// that is gone). The page goes back to draft, because the text on it is not what was approved. The
/// database holds the rule: one version per page, by unique index.
/// </remarks>
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

    /// <summary>
    /// Write the page's text. Whatever text the page had is deleted in the same write, and the page is a
    /// draft again. This is the one-at-a-time routes' path; a Generate's pieces go through
    /// <see cref="GccProjectPageRepository.SaveGeneratedAsync"/>, which does the same for every piece of a
    /// run together.
    /// </summary>
    public async Task<GccArtifactVersionDto?> CreateAsync(CreateGccArtifactVersionCommand command, CancellationToken ct = default)
    {
        var page = await _db.GccArtifacts.FirstOrDefaultAsync(a => a.Id == command.ArtifactId, ct);
        if (page is null) return null;

        var now = DateTime.UtcNow;
        await RemoveContentAsync(_db, page.Id, ct);
        page.Status = "draft";
        page.UpdatedAtUtc = now;

        var entity = new GccArtifactVersion
        {
            ArtifactId = page.Id,
            VersionNumber = 1,
            BodyJson = command.BodyDocumentJson,
            MetadataJson = command.MetadataJson,
            CreatedAtUtc = now,
        };

        _db.GccArtifactVersions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    /// <summary>
    /// Mark a page's content for deletion: its version, that version's evidence, and its approval
    /// events. Tracked only -- the caller saves, so a run's pieces and their replacements land in one
    /// write.
    /// </summary>
    internal static async Task RemoveContentAsync(ContentCreatorDbContext db, Guid artifactId, CancellationToken ct)
    {
        var versions = await db.GccArtifactVersions.Where(v => v.ArtifactId == artifactId).ToListAsync(ct);
        if (versions.Count == 0) return;

        var versionIds = versions.Select(v => v.Id).ToList();
        var evidence = await db.GccVersionEvidence.Where(e => versionIds.Contains(e.VersionId)).ToListAsync(ct);
        var approvals = await db.GccApprovalEvents.Where(e => versionIds.Contains(e.ArtifactVersionId)).ToListAsync(ct);

        db.GccApprovalEvents.RemoveRange(approvals);
        db.GccVersionEvidence.RemoveRange(evidence);
        db.GccArtifactVersions.RemoveRange(versions);
    }

    internal static GccArtifactVersionDto MapToDto(GccArtifactVersion entity) =>
        new(
            entity.Id,
            entity.ArtifactId,
            entity.VersionNumber,
            entity.BodyJson,
            entity.MetadataJson,
            entity.RowVersion,
            entity.CreatedAtUtc);
}
