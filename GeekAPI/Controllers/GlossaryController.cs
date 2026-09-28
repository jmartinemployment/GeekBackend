using GeekApplication.Interfaces;
using GeekApplication.Models.Glossary;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

/// <summary>
/// Glossary term reads. Public: <c>ApiKeyMiddleware.IsPublicGlossaryRead</c> admits GET on
/// these two routes and nothing else.
///
/// <para>
/// Read-only since 2026-09-28. The admin CRUD that lived here -- POST/PUT/DELETE on
/// <c>terms</c>, plus a revalidation ping to the site -- was removed when geekatyourspot
/// stopped reading the glossary from this API: its <c>src/lib/glossary.ts</c> now serves
/// the corpus from <c>src/data/glossary/terms.ts</c>, and says why -- "Editing a term is a
/// code change and a deploy, not a database update." A write path to a store that is no
/// longer the source of record is not an admin feature, it is a second truth.
/// </para>
///
/// <para>
/// The revalidation ping went with them for the same reason: the site's
/// <c>api/revalidate</c> route records that "no fetch carries that tag any more", so the
/// call could only read as a live cache that does not exist.
/// </para>
///
/// <para>
/// The writes still exist one layer down --
/// <c>HttpGlossaryRepository.CreateAsync/UpdateAsync/DeleteAsync</c> and
/// <c>repo/content/glossary</c> in GeekRepository -- and are now reachable from nothing.
/// Removing them is the rest of this cut, not a separate question.
/// </para>
/// </summary>
[ApiController]
[Route("api/glossary")]
public sealed class GlossaryController : ControllerBase
{
    private readonly IGlossaryRepository _glossary;

    public GlossaryController(IGlossaryRepository glossary) => _glossary = glossary;

    [HttpGet("terms")]
    public async Task<ActionResult<IReadOnlyList<GlossaryTermSummaryDto>>> GetAllPublished(
        CancellationToken ct = default)
    {
        var terms = await _glossary.GetAllPublishedAsync(ct);
        return Ok(terms);
    }

    [HttpGet("terms/{slug}")]
    public async Task<ActionResult<GlossaryTermDto>> GetBySlug(string slug, CancellationToken ct = default)
    {
        var term = await _glossary.GetBySlugAsync(slug, ct);
        return term is null ? NotFound() : Ok(term);
    }
}
