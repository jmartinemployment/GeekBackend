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
/// <b>A piece written from a page is saved under it</b> (2026-10-12). An email, a social post or an ad
/// that a Repurpose wrote from a pillar, a blog or a tool page names that page
/// (<see cref="GccGeneratedPiece.ParentPageId"/>). It is one of the page's pieces, found among them by
/// type and name as a page is found among the project's, and replaced the same way. It is not one of
/// the project's pages: the one-page rule and a Generate's delete do not see it, and it goes when its
/// page is written again or deleted. The database holds this rule too, one piece of a type and name
/// under a page, by unique index.
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
            .GroupBy(p => $"{p.ParentPageId}\u001f{PageKey(p)}", StringComparer.OrdinalIgnoreCase)
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

        // The project's own pages, and the pieces already under any page this run writes a piece from.
        var all = await _db.GccArtifacts.Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var pages = all.Where(a => a.ParentArtifactId == null).ToList();

        // A piece is written from one of this project's pages. One that names anything else -- a page
        // of another project, a page deleted while the run wrote, a piece under a page -- has nowhere
        // to be saved, and nothing of the run is.
        var orphan = command.Pieces.FirstOrDefault(p =>
            p.ParentPageId is Guid parent && !pages.Any(a => a.Id == parent));
        if (orphan is not null)
        {
            return GccGeneratedPiecesSaveResult.Refused(
                $"None of the {command.Pieces.Count} piece(s) was saved: the {orphan.Type.Trim()} "
                + $"'{orphan.Name.Trim()}' was written from page {orphan.ParentPageId}, which is not one of "
                + "this project's pages any more.");
        }

        var now = DateTime.UtcNow;
        var saved = new List<(GccArtifact Page, GccArtifactVersion Version, bool NewPage)>(command.Pieces.Count);

        foreach (var piece in command.Pieces)
        {
            // Among the project's pages, or among the pieces under the page this one was written from.
            var same = all
                .Where(a => a.ParentArtifactId == piece.ParentPageId
                    && string.Equals(PageKey(a), PageKey(piece), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (same.Count > 1)
            {
                // The database forbids this; if it is seen, nothing is guessed about which page to write.
                // Earlier pieces of this run may already have their page's old content marked for
                // deletion; a refusal writes nothing, so those marks go too.
                _db.ChangeTracker.Clear();
                return GccGeneratedPiecesSaveResult.Refused(
                    piece.ParentPageId is null
                        ? $"None of the {command.Pieces.Count} piece(s) was saved: the project has {same.Count} "
                            + $"{piece.Type.Trim()} pages named '{piece.Name.Trim()}', and a page is one."
                        : $"None of the {command.Pieces.Count} piece(s) was saved: page {piece.ParentPageId} has "
                            + $"{same.Count} {piece.Type.Trim()} pieces named '{piece.Name.Trim()}', and a piece is one.");
            }

            GccArtifact page;
            if (same.Count == 0)
            {
                page = new GccArtifact
                {
                    ProjectId = projectId,
                    CreateId = command.CreateId,
                    ParentArtifactId = piece.ParentPageId,
                    Type = piece.Type.Trim(),
                    Name = piece.Name.Trim(),
                    Status = "draft",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                _db.GccArtifacts.Add(page);
                all.Add(page);
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

    /// <summary>
    /// The project's pages of these types go, with their content and whatever was derived from them.
    /// </summary>
    /// <remarks>
    /// A Generate calls this before it writes anything (Jeff, 2026-10-06: "delete should happen first").
    /// The 8:26 run that morning was refused on its pillar and saved nothing, and the page then showed
    /// the previous afternoon's pillar as if it were the run's -- it was read as new, its faults were
    /// taken for the run's, and the fixes that had shipped between the two looked as if they had not.
    /// An old page left in place is a middle state; no page, with a failed job that says why, is not.
    /// </remarks>
    public async Task<GccPagesDeleteResult> DeletePagesAsync(
        Guid projectId, IReadOnlyList<string> types, IReadOnlyList<string>? names = null, CancellationToken ct = default)
    {
        if (!await _db.GccProjects.AnyAsync(p => p.Id == projectId, ct))
            return GccPagesDeleteResult.Missing();

        var wanted = types
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return new GccPagesDeleteResult(false, 0, 0);

        // Named pages only, when the run names them. A list with nothing in it is a run that writes
        // none of these pages, so none goes: it is never read as "every page".
        var named = names?
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (named is { Count: 0 }) return new GccPagesDeleteResult(false, 0, 0);

        var all = await _db.GccArtifacts.Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var pages = all
            .Where(a => a.ParentArtifactId == null && wanted.Contains(a.Type.Trim()))
            .Where(a => named is null || named.Contains(a.Name.Trim()))
            .ToList();
        if (pages.Count == 0) return new GccPagesDeleteResult(false, 0, 0);

        // A page derived from a deleted one has nothing to derive from, so it goes too -- and anything
        // derived from that, until nothing more hangs off the set.
        var going = pages.Select(a => a.Id).ToHashSet();
        var derived = new List<GccArtifact>();
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var child in all.Where(a => a.ParentArtifactId is Guid parent && going.Contains(parent) && !going.Contains(a.Id)))
            {
                going.Add(child.Id);
                derived.Add(child);
                grew = true;
            }
        }

        foreach (var id in going) await GccArtifactVersionRepository.RemoveContentAsync(_db, id, ct);
        _db.GccArtifacts.RemoveRange(pages);
        _db.GccArtifacts.RemoveRange(derived);
        await _db.SaveChangesAsync(ct);

        return new GccPagesDeleteResult(false, pages.Count, derived.Count);
    }

    /// <summary>
    /// The pieces of these types written from one page, deleted with their content. The page stands.
    /// </summary>
    /// <remarks>
    /// What a Repurpose does before it writes anything, for the reason a Generate deletes its pages
    /// first: a piece that is then refused must not leave the last run's piece to be read as this one's.
    /// </remarks>
    public async Task<GccPiecesDeleteResult> DeletePiecesAsync(
        Guid projectId, Guid pageId, IReadOnlyList<string> types, CancellationToken ct = default)
    {
        var isPage = await _db.GccArtifacts.AnyAsync(
            a => a.Id == pageId && a.ProjectId == projectId && a.ParentArtifactId == null, ct);
        if (!isPage) return GccPiecesDeleteResult.Missing();

        var wanted = types
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return new GccPiecesDeleteResult(false, 0);

        var under = await _db.GccArtifacts
            .Where(a => a.ProjectId == projectId && a.ParentArtifactId == pageId)
            .ToListAsync(ct);
        var pieces = under.Where(a => wanted.Contains(a.Type.Trim())).ToList();
        if (pieces.Count == 0) return new GccPiecesDeleteResult(false, 0);

        foreach (var piece in pieces) await GccArtifactVersionRepository.RemoveContentAsync(_db, piece.Id, ct);
        _db.GccArtifacts.RemoveRange(pieces);
        await _db.SaveChangesAsync(ct);

        return new GccPiecesDeleteResult(false, pieces.Count);
    }

    // The unit separator cannot occur in a type or a name, so two different pages never share a key.
    private static string PageKey(GccGeneratedPiece piece) => $"{piece.Type.Trim()}\u001f{piece.Name.Trim()}";

    private static string PageKey(GccArtifact page) => $"{page.Type.Trim()}\u001f{page.Name.Trim()}";
}
