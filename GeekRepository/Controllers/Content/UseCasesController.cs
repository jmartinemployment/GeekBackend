using GeekApplication.Entities;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/use-cases")]
public class UseCasesController : ControllerBase
{
    private readonly IUseCaseRepository _repo;
    private readonly IDepartmentRepository _departments;

    public UseCasesController(IUseCaseRepository repo, IDepartmentRepository departments)
    {
        _repo = repo;
        _departments = departments;
    }

    [HttpGet]
    public async Task<IReadOnlyList<UseCase>> GetAll() => await _repo.GetAllAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UseCase>> GetById(int id)
    {
        var u = await _repo.GetByIdAsync(id);
        return u is null ? NotFound() : Ok(u);
    }

    [HttpPost]
    public async Task<ActionResult<UseCase>> Create([FromBody] UseCase useCase)
    {
        var dept = await _departments.GetByIdAsync(useCase.DepartmentId);
        if (dept is null) return BadRequest($"Department {useCase.DepartmentId} not found.");
        useCase.CreatedAt = DateTime.UtcNow;
        useCase.UpdatedAt = DateTime.UtcNow;
        await _repo.AddAsync(useCase);
        return CreatedAtAction(nameof(GetById), new { id = useCase.Id }, useCase);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UseCase>> Update(int id, [FromBody] UseCase useCase)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return NotFound();
        var dept = await _departments.GetByIdAsync(useCase.DepartmentId);
        if (dept is null) return BadRequest($"Department {useCase.DepartmentId} not found.");
        useCase.Id = id;
        useCase.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(useCase);
        return Ok(useCase);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await _repo.DeleteAsync(id);
        return NoContent();
    }
}
