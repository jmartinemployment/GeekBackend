using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// A project's pages. One per content type and name -- one pillar, one blog, one tool page per
/// partner -- and each Generate replaces their content. Nothing older is kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>A re-run replaces the page</b> (Jeff, 2026-10-06: "no history is required ... the old created
/// content should be deleted"). Before 2026-10-05 every Generate created a second page beside the
/// last run's; from then until this, it kept the old text as the page's earlier version. Neither is
/// what the operator expects of running a project again. The page's old version goes, with its
/// evidence and its approval events, and the new text is the page's one version. The database holds
/// both rules: one page per type and name on a project, and one version per page, each by unique
/// index.
/// </para>
/// <para>
/// <b>Which page a piece belongs to</b> is its type and its name, compared without regard to case or
/// surrounding spaces, among the project's own pages (GR4) -- the ones no other page derives from. A
/// new page is keyed to the project and, until the create table goes, also stored under the create
/// the run names, which must be the project's.
/// </para>
/// <para>
/// <b>All or nothing</b> (A12). GeekAPI wrote each piece with two calls -- the draft, then its
/// version -- one piece after another, so a failure on the fourth tool page left three saved and a
/// run reported as failed. Every row of a run, and every deletion it entails, is one unit of work
/// saved once.
/// </para>
/// </remarks>
public class GccProjectPageRepository : IGccProjectPageRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccProjectPageRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccGeneratedPiecesSaveResult> SaveGeneratedAsync(
        Guid projectId, SaveGccGeneratedPiecesCommand command, CancellationToken ct = default)
    {
        if (!await _db.GccProjects.AnyAsync(p => p.Id == projectId && p.DeletedAtUtc == null, ct))
            return GccGeneratedPiecesSaveResult.Missing();

        if (command.Pieces.Count == 0)
            return GccGeneratedPiecesSaveResult.Refused("The run wrote no pieces, so there is nothing to save.");

        var unnamed = command.Pieces.FirstOrDefault(p =>
            string.IsNullOrWhiteSpace(p.Type) || string.IsNullOrWhiteSpace(p.Name));
        if (unnamed is not null)
        {
            return GccGeneratedPiecesSaveResult.Refused(
                $"None of the {command.Pieces.Count} piece(s) was saved: one has no type or no name "
                + $"(type '{unnamed.Type}', name '{unnamed.Name}'), so it belongs to no page.");
        }

        // A run writes each page once. Two pieces for one page would make the second the page's
        // content and the first text nobody asked for.
        var twice = command.Pieces
            .GroupBy(PageKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
        {
            var piece = twice.First();
            return GccGeneratedPiecesSaveResult.Refused(
                $"None of the {command.Pieces.Count} piece(s) was saved: the run wrote the {piece.Type.Trim()} "
                + $"'{piece.Name.Trim()}' {twice.Count()} times, and a page is written once per run.");
        }

        if (!await _db.GccCreates.AnyAsync(c => c.Id == command.CreateId && c.ProjectId == projectId, ct))
        {
            return GccGeneratedPiecesSaveResult.Refused(
                $"None of the {command.Pieces.Count} piece(s) was saved: create {command.CreateId} is not one "
                + "of this project's, so a new page cannot be stored under it.");
        }

        var pages = await _db.GccArtifacts
            .Where(a => a.ProjectId == projectId && a.ParentArtifactId == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        var saved = new List<(GccArtifact Page, GccArtifactVersion Version, bool NewPage)>(command.Pieces.Count);

        foreach (var piece in command.Pieces)
        {
            var same = pages.Where(a => string.Equals(PageKey(a), PageKey(piece), StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Count > 1)
            {
                // The database forbids this; if it is seen, nothing is guessed about which page to write.
                // Earlier pieces of this run may already have their page's old content marked for
                // deletion; a refusal writes nothing, so those marks go too.
                _db.ChangeTracker.Clear();
                return GccGeneratedPiecesSaveResult.Refused(
                    $"None of the {command.Pieces.Count} piece(s) was saved: the project has {same.Count} "
                    + $"{piece.Type.Trim()} pages named '{piece.Name.Trim()}', and a page is one.");
            }

            GccArtifact page;
            if (same.Count == 0)
            {
                page = new GccArtifact
                {
                    ProjectId = projectId,
                    CreateId = command.CreateId,
                    Type = piece.Type.Trim(),
                    Name = piece.Name.Trim(),
                    Status = "draft",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                _db.GccArtifacts.Add(page);
                pages.Add(page);
            }
            else
            {
                page = same[0];
                // The old text goes, with its evidence and its approvals. The text on the page is new
                // and nobody has approved it; the operator's current spelling of the name is the page's.
                await GccArtifactVersionRepository.RemoveContentAsync(_db, page.Id, ct);
                page.Status = "draft";
                page.Name = piece.Name.Trim();
                page.UpdatedAtUtc = now;
            }

            var version = new GccArtifactVersion
            {
                ArtifactId = page.Id,
                VersionNumber = 1,
                BodyJson = piece.BodyDocumentJson,
                MetadataJson = piece.MetadataJson,
                CreatedAtUtc = now,
            };
            _db.GccArtifactVersions.Add(version);
            saved.Add((page, version, same.Count == 0));
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            return GccGeneratedPiecesSaveResult.Refused(
                $"None of the {command.Pieces.Count} piece(s) was saved ("
                + string.Join(", ", command.Pieces.Select(p => $"{p.Type.Trim()} '{p.Name.Trim()}'"))
                + $"): {ex.GetBaseException().Message}");
        }

        return GccGeneratedPiecesSaveResult.Written(
            [.. saved.Select(s => new GccSavedPieceDto(
                GccArtifactRepository.MapToDto(s.Page, s.Version.VersionNumber, s.Version.CreatedAtUtc),
                GccArtifactVersionRepository.MapToDto(s.Version),
                s.NewPage))]);
    }

    // The unit separator cannot occur in a type or a name, so two different pages never share a key.
    private static string PageKey(GccGeneratedPiece piece) => $"{piece.Type.Trim()}\u001f{piece.Name.Trim()}";

    private static string PageKey(GccArtifact page) => $"{page.Type.Trim()}\u001f{page.Name.Trim()}";
}
