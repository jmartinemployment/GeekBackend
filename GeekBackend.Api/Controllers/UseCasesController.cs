using GeekBackend.Api.Dtos;
using GeekBackend.Data.Models;
using GeekBackend.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeekBackend.Api.Controllers;

[ApiController]
[Route("api/use-cases")]
public class UseCasesController : ControllerBase
{
    private readonly IUseCaseRepository _useCases;
    private readonly IDepartmentRepository _departments;

    public UseCasesController(IUseCaseRepository useCases, IDepartmentRepository departments)
    {
        _useCases = useCases;
        _departments = departments;
    }

    [HttpGet]
    public async Task<IReadOnlyList<UseCaseDto>> GetAll()
    {
        var all = await _useCases.GetAllAsync();
        return all.Select(u => new UseCaseDto(u.Id, u.DescriptiveName, u.Slug, u.Summary, u.CaseStudyId)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UseCaseDto>> GetById(int id)
    {
        var u = await _useCases.GetByIdAsync(id);
        if (u is null) return NotFound();
        return new UseCaseDto(u.Id, u.DescriptiveName, u.Slug, u.Summary, u.CaseStudyId);
    }

    [HttpPost]
    public async Task<ActionResult<UseCaseDto>> Create(UseCaseRequest req)
    {
        var dept = await _departments.GetByIdAsync(req.DepartmentId);
        if (dept is null) return BadRequest($"Department {req.DepartmentId} not found.");

        var useCase = new UseCase
        {
            DepartmentId = req.DepartmentId,
            CaseStudyId = req.CaseStudyId,
            DescriptiveName = req.DescriptiveName,
            Slug = req.Slug,
            Summary = req.Summary,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _useCases.AddAsync(useCase);
        var dto = new UseCaseDto(useCase.Id, useCase.DescriptiveName, useCase.Slug, useCase.Summary, useCase.CaseStudyId);
        return CreatedAtAction(nameof(GetById), new { id = useCase.Id }, dto);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UseCaseDto>> Update(int id, UseCaseRequest req)
    {
        var useCase = await _useCases.GetByIdAsync(id);
        if (useCase is null) return NotFound();

        var dept = await _departments.GetByIdAsync(req.DepartmentId);
        if (dept is null) return BadRequest($"Department {req.DepartmentId} not found.");

        useCase.DepartmentId = req.DepartmentId;
        useCase.CaseStudyId = req.CaseStudyId;
        useCase.DescriptiveName = req.DescriptiveName;
        useCase.Slug = req.Slug;
        useCase.Summary = req.Summary;
        useCase.UpdatedAt = DateTime.UtcNow;

        await _useCases.UpdateAsync(useCase);
        return new UseCaseDto(useCase.Id, useCase.DescriptiveName, useCase.Slug, useCase.Summary, useCase.CaseStudyId);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var useCase = await _useCases.GetByIdAsync(id);
        if (useCase is null) return NotFound();

        await _useCases.DeleteAsync(id);
        return NoContent();
    }
}
