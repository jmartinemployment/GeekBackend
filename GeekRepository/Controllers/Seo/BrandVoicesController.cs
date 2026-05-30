using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/brand-voices")]
public sealed class BrandVoicesController(IBrandVoiceRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await repo.ListByUserAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await repo.GetByIdAsync(id, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        if (result.Value!.UserId != userId)
            return Forbid();
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] Guid userId, [FromBody] CreateBrandVoiceRequest request, CancellationToken ct)
    {
        var result = await repo.CreateAsync(userId, request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id, userId }, result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromQuery] Guid userId, [FromBody] UpdateBrandVoiceRequest request, CancellationToken ct)
    {
        var result = await repo.UpdateAsync(userId, id, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await repo.DeleteAsync(userId, id, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
