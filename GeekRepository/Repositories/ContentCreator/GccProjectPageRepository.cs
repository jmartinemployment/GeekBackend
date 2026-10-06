using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// A project's pages. One per content type and name -- one pillar, one blog, one tool page per
/// partner -- and each Generate rewrites them as new versions.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Generate adds versions to the project"</b> (fix-project-persistence J1). It did not: every
/// Generate created a new draft for every piece, beside the last run's, under the same name. On
/// 2026-10-05 project "test" held two pillars, two blogs and nine tool pages after two runs; the page
/// showed the morning's as if it were the afternoon's, and the export held both with "-2" on the older
/// file (Jeff: "you surprised me keeping old version that wasn't clear or expected, what do I do with
/// two versions").
/// </para>
/// <para>
/// <b>Which page a piece belongs to</b> is its type and its name, compared without regard to case, among
/// the project's pages -- the drafts keyed to it (GR4). A new page is keyed to the project and, until
/// the create table goes, also stored under the create the run names, which must be the project's.
/// </para>
/// <para>
/// <b>All or nothing</b> (A12). GeekAPI wrote each piece with two calls -- the draft, then its
/// version -- one piece after another, so a failure on the fourth tool page left three saved and a
/// run reported as failed. Every row of a run is added to one unit of work and saved once.
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
        // newest version and the first a version nobody asked for.
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

        var drafts = await _db.GccArtifacts.Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var saved = new List<(GccArtifact Page, GccArtifactVersion Version, bool NewPage)>(command.Pieces.Count);

        foreach (var piece in command.Pieces)
        {
            var same = DraftsOfPage(drafts, PageKey(piece));
            GccArtifact page;
            int number;
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
                drafts.Add(page);
                number = 1;
            }
            else
            {
                page = same[0];
                // More than one draft of this page is what earlier Generates left. They become this
                // page's earlier versions here, in the same write, so the run's version is the newest
                // of one page rather than of whichever duplicate was picked.
                var highest = same.Count > 1
                    ? await MergeAsync(page, same.Skip(1).ToList(), drafts, ct)
                    : await _db.GccArtifactVersions
                        .Where(v => v.ArtifactId == page.Id)
                        .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
                number = highest + 1;

                // The text on the page is new and nobody has approved it; the operator's current
                // spelling of the name is the page's name.
                page.Status = "draft";
                page.Name = piece.Name.Trim();
                page.UpdatedAtUtc = now;
            }

            var version = new GccArtifactVersion
            {
                ArtifactId = page.Id,
                VersionNumber = number,
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

    public async Task<GccDraftMergeResult> MergeDuplicateDraftsAsync(Guid projectId, CancellationToken ct = default)
    {
        if (!await _db.GccProjects.AnyAsync(p => p.Id == projectId, ct))
            return GccDraftMergeResult.Missing();

        var drafts = await _db.GccArtifacts.Where(a => a.ProjectId == projectId).ToListAsync(ct);

        var pages = 0;
        var merged = 0;
        var moved = 0;
        foreach (var key in drafts.Select(PageKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            var same = DraftsOfPage(drafts, key);
            if (same.Count < 2) continue;

            var others = same.Skip(1).ToList();
            var otherIds = others.Select(a => a.Id).ToList();
            moved += await _db.GccArtifactVersions.CountAsync(v => otherIds.Contains(v.ArtifactId), ct);
            await MergeAsync(same[0], others, drafts, ct);
            pages++;
            merged += others.Count;
        }

        if (pages > 0) await _db.SaveChangesAsync(ct);
        return new GccDraftMergeResult(false, pages, merged, moved);
    }

    /// <summary>
    /// Make <paramref name="others"/> earlier versions of <paramref name="page"/>: every version of
    /// all of them is renumbered in the order it was written and belongs to the page, anything
    /// derived from one of the others points at the page, and the others are removed. Returns the
    /// page's highest version number afterwards. Tracked only -- the caller saves.
    /// </summary>
    private async Task<int> MergeAsync(
        GccArtifact page, List<GccArtifact> others, List<GccArtifact> drafts, CancellationToken ct)
    {
        var ids = others.Select(a => a.Id).Append(page.Id).ToList();
        var versions = await _db.GccArtifactVersions.Where(v => ids.Contains(v.ArtifactId)).ToListAsync(ct);
        var inOrder = versions
            .OrderBy(v => v.CreatedAtUtc)
            .ThenBy(v => v.VersionNumber)
            .ThenBy(v => v.Id)
            .ToList();
        for (var i = 0; i < inOrder.Count; i++)
        {
            inOrder[i].ArtifactId = page.Id;
            inOrder[i].VersionNumber = i + 1;
        }

        var otherIds = others.Select(a => a.Id).ToList();
        var derived = await _db.GccArtifacts
            .Where(a => a.ParentArtifactId != null && otherIds.Contains(a.ParentArtifactId.Value))
            .ToListAsync(ct);
        foreach (var child in derived) child.ParentArtifactId = page.Id;

        _db.GccArtifacts.RemoveRange(others);
        drafts.RemoveAll(others.Contains);
        return inOrder.Count;
    }

    /// <summary>The drafts of one page, newest first: the first is the page, the rest are duplicates of it.</summary>
    private static List<GccArtifact> DraftsOfPage(List<GccArtifact> drafts, string pageKey) =>
        [.. drafts
            .Where(a => string.Equals(PageKey(a), pageKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)];

    // The unit separator cannot occur in a type or a name, so two different pages never share a key.
    private static string PageKey(GccGeneratedPiece piece) => $"{piece.Type.Trim()}\u001f{piece.Name.Trim()}";

    private static string PageKey(GccArtifact draft) => $"{draft.Type.Trim()}\u001f{draft.Name.Trim()}";
}
