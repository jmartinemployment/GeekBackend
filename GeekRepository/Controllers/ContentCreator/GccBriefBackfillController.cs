using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// The brief backfill (GR3), as its dry-run report. A GET that reads and writes nothing; the backfill
/// itself has no route here until the report has been read and a backup taken.
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
}
