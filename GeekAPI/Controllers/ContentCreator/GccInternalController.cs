using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreator;

/// <summary>
/// Content Creator operations for the operator and internal services, behind the internal key
/// (ApiKeyMiddleware's <c>/api/{x}/internal/*</c> convention), never a browser session.
/// </summary>
[ApiController]
[Route("api/geek-content-creator/internal")]
public sealed class GccInternalController(HttpGccRepository repo) : ControllerBase
{
    /// <summary>
    /// What the brief backfill (GR3) would do, project by project, and the creates with no project.
    /// Reads only. Read before the backfill runs, after a Supabase backup has been taken.
    /// </summary>
    [HttpGet("brief-backfill/report")]
    public async Task<ActionResult<GccBriefBackfillReport>> BriefBackfillReport(CancellationToken ct) =>
        Ok(await repo.GetBriefBackfillReportAsync(ct));

    /// <summary>
    /// Copy one project's brief and keyword onto it from its one create, as a backfill revision.
    /// Refused -- 409 with the reason, nothing written -- unless the project has no brief of its own
    /// and exactly one create, which carries a brief. The create is left as it is.
    /// </summary>
    [HttpPost("brief-backfill/projects/{projectId:guid}")]
    public async Task<IActionResult> CopyBriefOntoProject(Guid projectId, CancellationToken ct)
    {
        var result = await repo.CopyBriefOntoProjectAsync(projectId, ct);
        if (result is null) return NotFound();
        if (result.Refusal is { } refusal) return Conflict(refusal);
        return Ok(new
        {
            projectId = result.Project!.Id,
            projectName = result.Project.Name,
            topic = result.Project.Topic,
            version = result.Project.Version,
            revisionId = result.Revision!.Id,
            revisionKind = result.Revision.Kind,
            revisionSavedAtUtc = result.Revision.SavedAtUtc,
        });
    }
}
