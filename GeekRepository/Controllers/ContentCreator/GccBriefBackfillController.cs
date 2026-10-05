using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// The brief backfill (GR3): the report, which reads only, and the copy for one named project.
/// </summary>
[ApiController]
[Route("repo/content-creator/brief-backfill")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccBriefBackfillController : ControllerBase
{
    private readonly IGccBriefBackfillRepository _backfill;

    public GccBriefBackfillController(IGccBriefBackfillRepository backfill) => _backfill = backfill;

    [HttpGet("report")]
    public async Task<ActionResult<GccBriefBackfillReport>> Report(CancellationToken ct) =>
        Ok(await _backfill.GetReportAsync(ct));

    /// <summary>
    /// Copy one project's brief onto it from its one create. 200 with the result either way once the
    /// project exists: a refusal is an answer, with its reason, and writes nothing.
    /// </summary>
    [HttpPost("projects/{projectId:guid}")]
    public async Task<ActionResult<GccBriefCopyResult>> Copy(Guid projectId, CancellationToken ct)
    {
        var result = await _backfill.CopyOntoProjectAsync(projectId, ct);
        return result.NotFound ? NotFound() : Ok(result);
    }
}
