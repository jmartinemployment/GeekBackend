using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccVersionEvidenceRepository(ContentCreatorDbContext db) : IGccVersionEvidenceRepository
{
    public async Task<GccVersionEvidenceDto?> GetByVersionIdAsync(Guid versionId, CancellationToken ct = default)
    {
        var row = await db.GccVersionEvidence.AsNoTracking().FirstOrDefaultAsync(e => e.VersionId == versionId, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<GccVersionEvidenceResult> CreateAsync(
        CreateGccVersionEvidenceCommand command, CancellationToken ct = default)
    {
        if (!await db.GccArtifactVersions.AnyAsync(v => v.Id == command.VersionId, ct))
            return GccVersionEvidenceResult.Refused($"Version {command.VersionId} does not exist.");

        // The project is the version's own: version -> draft -> project (GR4). Read here, so a caller
        // cannot file one project's evidence under another.
        var projectId = await (
            from version in db.GccArtifactVersions
            join artifact in db.GccArtifacts on version.ArtifactId equals artifact.Id
            where version.Id == command.VersionId
            select artifact.ProjectId).FirstOrDefaultAsync(ct);
        if (projectId is not Guid project)
        {
            return GccVersionEvidenceResult.Refused(
                $"Version {command.VersionId}'s draft is not on a project, so its evidence has nowhere to be kept.");
        }

        if (await db.GccVersionEvidence.AnyAsync(e => e.VersionId == command.VersionId, ct))
            return GccVersionEvidenceResult.Refused(
                $"Version {command.VersionId} already has its evidence. A version is made once, so it is recorded once.");

        var row = new GccVersionEvidence
        {
            VersionId = command.VersionId,
            ProjectId = project,
            Provider = command.Provider,
            ModelIdsJson = command.ModelIdsJson,
            CallsJson = command.CallsJson,
            DiscardedDraftsJson = command.DiscardedDraftsJson,
            ResearchJson = command.ResearchJson,
            PassagesJson = command.PassagesJson,
            QuoteCandidatesJson = command.QuoteCandidatesJson,
            ReadinessJson = command.ReadinessJson,
            BankDigestsJson = command.BankDigestsJson,
            RagQueriesJson = command.RagQueriesJson,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.GccVersionEvidence.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index on version_id: a second writer between the check above and this save.
            return GccVersionEvidenceResult.Refused(
                $"Version {command.VersionId} already has its evidence. A version is made once, so it is recorded once.");
        }

        return GccVersionEvidenceResult.Written(ToDto(row));
    }

    private static GccVersionEvidenceDto ToDto(GccVersionEvidence e) => new(
        e.Id, e.VersionId, e.ProjectId, e.Provider, e.ModelIdsJson, e.CallsJson, e.DiscardedDraftsJson, e.ResearchJson,
        e.PassagesJson, e.QuoteCandidatesJson, e.ReadinessJson, e.BankDigestsJson, e.RagQueriesJson, e.CreatedAtUtc);
}
