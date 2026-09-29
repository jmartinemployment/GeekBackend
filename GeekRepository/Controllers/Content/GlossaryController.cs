using GeekApplication.Interfaces;
using GeekApplication.Models.Glossary;
using GeekRepository.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/glossary")]
public sealed class GlossaryController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGlossaryRepository _glossary;

    public GlossaryController(IUnitOfWork unitOfWork, IGlossaryRepository glossary)
    {
        _unitOfWork = unitOfWork;
        _glossary = glossary;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GlossaryTermSummaryDto>>> GetAllPublished(
        CancellationToken ct = default)
    {
        IReadOnlyList<GlossaryTermSummaryDto> results = [];

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            results = await _glossary.GetAllPublishedAsync(ct);
        }, ct);

        return Ok(results);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<GlossaryTermDto>> GetBySlug(string slug, CancellationToken ct = default)
    {
        GlossaryTermDto? term = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            term = await _glossary.GetBySlugAsync(slug, ct);
        }, ct);

        return term is null ? NotFound() : Ok(term);
    }
}
