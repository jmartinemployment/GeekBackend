using GeekApplication.Entities;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/departments")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentRepository _repo;
    private readonly IUseCaseRepository _useCases;

    public DepartmentsController(IDepartmentRepository repo, IUseCaseRepository useCases)
    {
        _repo = repo;
        _useCases = useCases;
    }

    [HttpGet("with-use-cases")]
    public async Task<IReadOnlyList<Department>> GetWithUseCasesAndCaseStudies() =>
        await _repo.GetWithUseCasesAndCaseStudiesAsync();

    [HttpGet]
    public async Task<IReadOnlyList<Department>> GetAll() => await _repo.GetAllAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Department>> GetById(int id)
    {
        var d = await _repo.GetByIdAsync(id);
        return d is null ? NotFound() : Ok(d);
    }

    [HttpGet("{id:int}/use-cases")]
    public async Task<ActionResult<IReadOnlyList<UseCase>>> GetUseCases(int id)
    {
        var dept = await _repo.GetByIdAsync(id);
        if (dept is null) return NotFound();
        var useCases = await _useCases.GetByDepartmentIdAsync(id);
        return Ok(useCases);
    }

    [HttpPost]
    public async Task<ActionResult<Department>> Create([FromBody] Department department)
    {
        department.CreatedAt = DateTime.UtcNow;
        await _repo.AddAsync(department);
        return CreatedAtAction(nameof(GetById), new { id = department.Id }, department);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Department>> Update(int id, [FromBody] Department department)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return NotFound();
        department.Id = id;
        await _repo.UpdateAsync(department);
        return Ok(department);
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
