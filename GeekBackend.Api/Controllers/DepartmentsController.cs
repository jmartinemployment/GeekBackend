using GeekBackend.Api.Dtos;
using GeekBackend.Data.Data;
using GeekBackend.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUseCaseRepository _useCases;

    public DepartmentsController(AppDbContext db, IUseCaseRepository useCases)
    {
        _db = db;
        _useCases = useCases;
    }

    [HttpGet]
    public async Task<IReadOnlyList<DepartmentDto>> GetAll() =>
        await _db.Departments
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Name, d.Slug, d.Description, d.IconName, d.SortOrder))
            .ToListAsync();

    [HttpGet("{id:int}/use-cases")]
    public async Task<ActionResult<IReadOnlyList<UseCaseDto>>> GetUseCases(int id)
    {
        var exists = await _db.Departments.AnyAsync(d => d.Id == id);
        if (!exists) return NotFound();

        var useCases = await _useCases.GetByDepartmentIdAsync(id);
        return Ok(useCases.Select(u => new UseCaseDto(u.Id, u.DescriptiveName, u.Slug, u.Summary, u.CaseStudyId)).ToList());
    }
}
