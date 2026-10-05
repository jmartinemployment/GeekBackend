using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// The brief backfill (GR3): the report of what every project holds, and the copy for one project.
/// </summary>
/// <remarks>
/// The report reads only. It showed one project worth copying -- one keyword, one brief (Jeff,
/// 2026-10-05: "hardly worth the trouble") -- so there is no backfill of everything, and none of the
/// decisions one would need about deleted projects, mixed keywords and creates with no project.
/// <see cref="CopyOntoProjectAsync"/> does the one unambiguous case and refuses every other.
/// </remarks>
public class GccBriefBackfillRepository : IGccBriefBackfillRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccBriefBackfillRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccBriefBackfillReport> GetReportAsync(CancellationToken ct = default)
    {
        var projects = await _db.GccProjects.AsNoTracking().ToListAsync(ct);
        var creates = await _db.GccCreates.AsNoTracking().ToListAsync(ct);
        var clientNames = await _db.GccClients.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var artifactsByCreate = await _db.GccArtifacts.AsNoTracking()
            .GroupBy(a => a.CreateId)
            .Select(g => new { CreateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CreateId, x => x.Count, ct);
        var revisionsByProject = await _db.GccProjectRevisions.AsNoTracking()
            .GroupBy(r => r.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count(), Newest = g.Max(r => r.SavedAtUtc) })
            .ToDictionaryAsync(x => x.ProjectId, x => (x.Count, x.Newest), ct);

        string Client(Guid id) => clientNames.TryGetValue(id, out var name) ? name : "(client not found)";
        int Artifacts(Guid createId) => artifactsByCreate.TryGetValue(createId, out var n) ? n : 0;

        var byProject = creates
            .Where(c => c.ProjectId is not null)
            .GroupBy(c => c.ProjectId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAtUtc).ToList());

        var reported = new List<GccBriefBackfillProject>();
        foreach (var project in projects.Where(p => byProject.ContainsKey(p.Id)).OrderBy(p => p.Name))
        {
            var ordered = byProject[project.Id];
            var revisions = revisionsByProject.TryGetValue(project.Id, out var r) ? r : (Count: 0, Newest: default);
            var hasBrief = !string.IsNullOrWhiteSpace(project.BriefJson);
            var newest = ordered[0];

            var decisions = new List<string>();
            if (hasBrief || revisions.Count > 0)
            {
                decisions.Add(
                    "The project already has a brief of its own"
                    + (revisions.Count > 0 ? $", saved {revisions.Newest:u}" : string.Empty)
                    + ". Copying the create's brief onto it would replace that one.");
            }

            if (string.IsNullOrWhiteSpace(newest.BriefJson) && ordered.Skip(1).Any(c => !string.IsNullOrWhiteSpace(c.BriefJson)))
            {
                decisions.Add(
                    $"The newest create (\"{newest.Topic}\", {newest.CreatedAtUtc:u}) has no brief, and an older "
                    + "one does. The rule takes the newest, which would leave the project with no brief.");
            }

            // A project is one keyword. Found in production on 2026-10-05: a project whose newest create
            // is one keyword and whose drafts all sit under an older create with another.
            var keywords = ordered
                .Select(c => c.Topic.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keywords.Count > 1)
            {
                decisions.Add(
                    $"Its creates carry {keywords.Count} different keywords: "
                    + string.Join("; ", keywords.Select(k => $"\"{k}\" ({ordered.Where(c => string.Equals(c.Topic.Trim(), k, StringComparison.OrdinalIgnoreCase)).Sum(c => Artifacts(c.Id))} drafts)"))
                    + $". A project is one keyword, and the rule would make it \"{newest.Topic}\".");
            }

            if (project.DeletedAtUtc is not null)
                decisions.Add("The project is deleted. Its creates and their drafts still exist.");

            reported.Add(new GccBriefBackfillProject(
                project.Id,
                project.Name,
                Client(project.ClientId),
                project.DeletedAtUtc is not null,
                hasBrief,
                project.BriefVersion,
                revisions.Count > 0 ? revisions.Newest : null,
                revisions.Count,
                decisions,
                ordered.Select((c, i) => new GccBriefBackfillCreate(
                    c.Id,
                    c.Topic,
                    c.StartingContentType,
                    c.CreatedAtUtc,
                    c.UpdatedAtUtc,
                    !string.IsNullOrWhiteSpace(c.BriefJson),
                    !string.IsNullOrWhiteSpace(c.ResearchJson),
                    !string.IsNullOrWhiteSpace(c.SiteSectionJson),
                    Artifacts(c.Id),
                    i == 0 ? GccBriefBackfillRoles.Current : GccBriefBackfillRoles.Revision)).ToList()));
        }

        var unassigned = creates
            .Where(c => c.ProjectId is null)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new GccBriefBackfillUnassignedCreate(
                c.Id, Client(c.ClientId), c.Topic, c.CreatedAtUtc, c.UpdatedAtUtc, Artifacts(c.Id)))
            .ToList();

        var counts = new GccBriefBackfillCounts(
            Projects: projects.Count,
            ProjectsWithCreates: reported.Count,
            ProjectsNeedingADecision: reported.Count(p => p.Decisions.Count > 0),
            Creates: creates.Count,
            CreatesOnProjects: creates.Count - unassigned.Count,
            CreatesUnassigned: unassigned.Count,
            Artifacts: artifactsByCreate.Values.Sum(),
            Revisions: revisionsByProject.Values.Sum(v => v.Count));

        return new GccBriefBackfillReport(DateTime.UtcNow, counts, reported, unassigned);
    }

    /// <remarks>
    /// The create is not changed and not deleted: it still holds the project's drafts, and it is what
    /// the create-keyed page reads until that page is retired.
    ///
    /// The revision is the create's brief as its owner last saved it, so it carries the create's
    /// updated-at and owner rather than the moment and identity of whoever ran the copy.
    /// </remarks>
    public async Task<GccBriefCopyResult> CopyOntoProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.DeletedAtUtc == null, ct);
        if (project is null) return GccBriefCopyResult.Missing();

        if (!string.IsNullOrWhiteSpace(project.BriefJson)
            || project.BriefVersion != 0
            || await _db.GccProjectRevisions.AnyAsync(r => r.ProjectId == projectId, ct))
        {
            return GccBriefCopyResult.Refused(
                $"Project \"{project.Name}\" already has a brief of its own. Nothing was copied over it.");
        }

        var creates = await _db.GccCreates.AsNoTracking().Where(c => c.ProjectId == projectId).ToListAsync(ct);
        if (creates.Count != 1)
        {
            return GccBriefCopyResult.Refused(
                $"Project \"{project.Name}\" has {creates.Count} creates. This copies a project that has "
                + "exactly one, where there is nothing to choose between.");
        }

        var create = creates[0];
        if (string.IsNullOrWhiteSpace(create.BriefJson))
        {
            return GccBriefCopyResult.Refused(
                $"Project \"{project.Name}\"'s create (\"{create.Topic}\") has no brief to copy.");
        }

        var topic = string.IsNullOrWhiteSpace(create.Topic) ? null : create.Topic.Trim();
        project.BriefJson = create.BriefJson;
        project.Topic = topic;
        project.ResearchJson = create.ResearchJson;
        project.SiteSectionJson = create.SiteSectionJson;
        project.BriefVersion = 1;
        project.UpdatedAtUtc = DateTime.UtcNow;

        var revision = new Data.Entities.ContentCreator.GccProjectRevision
        {
            ProjectId = project.Id,
            Kind = GccProjectRevisionKinds.Backfill,
            BriefJson = create.BriefJson,
            Topic = topic,
            SavedBy = create.OwnerUserId.ToString(),
            SavedAtUtc = create.UpdatedAtUtc,
        };
        _db.GccProjectRevisions.Add(revision);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The project changed between the read above and this write -- a brief saved on it, most
            // likely. Nothing of the copy was written.
            _db.ChangeTracker.Clear();
            return GccBriefCopyResult.Refused(
                $"Project \"{project.Name}\" changed while its brief was being copied. Nothing was copied.");
        }

        await transaction.CommitAsync(ct);

        return GccBriefCopyResult.Copied(
            new GccProjectDto(
                project.Id, project.ClientId, project.Name, project.Code, project.Description, project.Status,
                project.SiteUrl, project.ProjectSiteRunId, project.Department,
                project.PartnerUrls.AsReadOnly(), project.CompetitorUrls.AsReadOnly(),
                project.StartDate, project.DueDate, project.FinishedDate, project.EstimatedHours, project.Budget,
                project.BudgetCurrency?.Trim(), project.CreatedAtUtc, project.UpdatedAtUtc,
                project.BriefJson, project.Topic, project.ResearchJson, project.SiteSectionJson,
                project.BriefVersion, revision.SavedAtUtc),
            new GccProjectRevisionDto(
                revision.Id, revision.ProjectId, revision.Kind, revision.BriefJson, revision.Topic,
                revision.SavedBy, revision.SavedAtUtc));
    }
}
