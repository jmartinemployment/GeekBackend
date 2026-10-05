using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// The dry run of the brief backfill (GR3): what would be copied onto each project, and what the plan
/// leaves undecided. Every query is no-tracking and nothing is saved.
/// </summary>
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
}
