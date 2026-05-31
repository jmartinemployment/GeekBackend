using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/plagiarism")]
public sealed class PlagiarismController(
    IPlagiarismRepository plagiarism,
    IContentDocumentService documents) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLatest([FromQuery] Guid documentId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var access = await documents.EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess)
            return access.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(access.Error);

        var result = await plagiarism.GetLatestByDocumentAsync(documentId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] Guid userId, [FromBody] SeoPlagiarismCheck check, CancellationToken ct)
    {
        var access = await documents.EnsureAccessAsync(userId, check.DocumentId, ct);
        if (!access.IsSuccess)
            return access.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(access.Error);

        if (check.Id == Guid.Empty)
            check.Id = Guid.NewGuid();

        var result = await plagiarism.CreateAsync(check, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
