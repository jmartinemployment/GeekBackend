using GeekApplication.Interfaces;
using GeekApplication.Models.Glossary;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

/// <summary>
/// Glossary term API: public reads and admin CRUD (writes require X-API-Key).
/// </summary>
[ApiController]
[Route("api/glossary")]
public sealed class GlossaryController : ControllerBase
{
    private readonly IGlossaryRepository _glossary;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GlossaryController(
        IGlossaryRepository glossary,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _glossary = glossary;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

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

    [HttpPost("terms")]
    public async Task<ActionResult<GlossaryTermDto>> Create(
        [FromBody] GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        var created = await _glossary.CreateAsync(request, ct);
        await TriggerRevalidationAsync(ct);
        return CreatedAtAction(nameof(GetBySlug), new { slug = created.Slug }, created);
    }

    [HttpPut("terms/{slug}")]
    public async Task<ActionResult<GlossaryTermDto>> Update(
        string slug,
        [FromBody] GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        var updated = await _glossary.UpdateAsync(slug, request, ct);
        if (updated is null) return NotFound();
        await TriggerRevalidationAsync(ct);
        return Ok(updated);
    }

    [HttpDelete("terms/{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct = default)
    {
        var deleted = await _glossary.DeleteAsync(slug, ct);
        if (!deleted) return NotFound();
        await TriggerRevalidationAsync(ct);
        return NoContent();
    }

    private async Task TriggerRevalidationAsync(CancellationToken ct)
    {
        var siteUrl = _configuration["GlossaryRevalidation:SiteUrl"];
        var secret = _configuration["GlossaryRevalidation:Secret"];
        if (string.IsNullOrWhiteSpace(siteUrl) || string.IsNullOrWhiteSpace(secret))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{siteUrl.TrimEnd('/')}/api/revalidate");
            request.Headers.Add("Authorization", $"Bearer {secret}");
            request.Content = JsonContent.Create(new { tag = "glossary" });
            await client.SendAsync(request, ct);
        }
        catch
        {
            // Revalidation is best-effort; do not fail the write.
        }
    }
}
