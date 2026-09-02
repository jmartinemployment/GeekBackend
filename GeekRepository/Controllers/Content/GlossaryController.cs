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

    [HttpPost]
    public async Task<ActionResult<GlossaryTermDto>> Create(
        [FromBody] GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        GlossaryTermDto created = null!;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            created = await _glossary.CreateAsync(request, ct);
        }, ct);

        return CreatedAtAction(nameof(GetBySlug), new { slug = created.Slug }, created);
    }

    [HttpPut("{slug}")]
    public async Task<ActionResult<GlossaryTermDto>> Update(
        string slug,
        [FromBody] GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        GlossaryTermDto? updated = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            updated = await _glossary.UpdateAsync(slug, request, ct);
        }, ct);

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct = default)
    {
        var deleted = false;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            deleted = await _glossary.DeleteAsync(slug, ct);
        }, ct);

        return deleted ? NoContent() : NotFound();
    }
}
