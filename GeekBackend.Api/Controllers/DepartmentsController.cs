using GeekBackend.Api.Dtos;
using GeekBackend.Data.Models;
using GeekBackend.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeekBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentRepository _departments;
    private readonly IUseCaseRepository _useCases;

    public DepartmentsController(IDepartmentRepository departments, IUseCaseRepository useCases)
    {
        _departments = departments;
        _useCases = useCases;
    }

    [HttpGet]
    public async Task<IReadOnlyList<DepartmentDto>> GetAll()
    {
        var all = await _departments.GetAllAsync();
        return all.Select(d => new DepartmentDto(d.Id, d.Name, d.Slug, d.Description, d.IconName, d.SortOrder)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentDto>> GetById(int id)
    {
        var d = await _departments.GetByIdAsync(id);
        if (d is null) return NotFound();
        return new DepartmentDto(d.Id, d.Name, d.Slug, d.Description, d.IconName, d.SortOrder);
    }

    [HttpGet("{id:int}/use-cases")]
    public async Task<ActionResult<IReadOnlyList<UseCaseDto>>> GetUseCases(int id)
    {
        var dept = await _departments.GetByIdAsync(id);
        if (dept is null) return NotFound();

        var useCases = await _useCases.GetByDepartmentIdAsync(id);
        return Ok(useCases.Select(u => new UseCaseDto(u.Id, u.DescriptiveName, u.Slug, u.Summary, u.CaseStudyId)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentRequest req)
    {
        var department = new Department
        {
            Name = req.Name,
            Slug = req.Slug,
            Description = req.Description,
            IconName = req.IconName,
            SortOrder = req.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        await _departments.AddAsync(department);
        var dto = new DepartmentDto(department.Id, department.Name, department.Slug, department.Description, department.IconName, department.SortOrder);
        return CreatedAtAction(nameof(GetById), new { id = department.Id }, dto);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentDto>> Update(int id, DepartmentRequest req)
    {
        var department = await _departments.GetByIdAsync(id);
        if (department is null) return NotFound();

        department.Name = req.Name;
        department.Slug = req.Slug;
        department.Description = req.Description;
        department.IconName = req.IconName;
        department.SortOrder = req.SortOrder;

        await _departments.UpdateAsync(department);
        return new DepartmentDto(department.Id, department.Name, department.Slug, department.Description, department.IconName, department.SortOrder);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var department = await _departments.GetByIdAsync(id);
        if (department is null) return NotFound();

        await _departments.DeleteAsync(id);
        return NoContent();
    }
}
