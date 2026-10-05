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
}
