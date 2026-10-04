using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// The bank of partner extractions, read before a generate pays for one and written after it does.
/// </summary>
[ApiController]
[Route("repo/content-creator/partner-extractions")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccPartnerExtractionsController(IGccPartnerExtractionBankRepository repository) : ControllerBase
{
    [HttpGet("{partnerHost}/{pagesDigest}")]
    public async Task<ActionResult<GccBankedPartnerExtractionDto>> Find(
        string partnerHost, string pagesDigest, CancellationToken ct)
    {
        var banked = await repository.FindAsync(partnerHost, pagesDigest, ct);
        return banked is null ? NotFound() : Ok(banked);
    }

    [HttpPost]
    public async Task<ActionResult<GccBankedPartnerExtractionDto>> Bank(
        [FromBody] BankGccPartnerExtractionCommand request, CancellationToken ct)
    {
        if (request is null) return BadRequest("Body required");
        if (string.IsNullOrWhiteSpace(request.PartnerHost)) return BadRequest("partnerHost required");
        if (string.IsNullOrWhiteSpace(request.PagesDigest)) return BadRequest("pagesDigest required");
        if (string.IsNullOrWhiteSpace(request.ExtractionJson)) return BadRequest("extractionJson required");

        return Ok(await repository.UpsertAsync(request, ct));
    }
}
