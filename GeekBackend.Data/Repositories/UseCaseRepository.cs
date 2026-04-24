using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Data.Repositories;

public class UseCaseRepository : IUseCaseRepository
{
    private readonly AppDbContext _context;

    public UseCaseRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UseCase?> GetByIdAsync(int id) =>
        await _context.UseCases
            .Include(u => u.Department)
            .Include(u => u.CaseStudy)
            .FirstOrDefaultAsync(u => u.Id == id);

    public async Task<UseCase?> GetBySlugAsync(string slug) =>
        await _context.UseCases
            .Include(u => u.Department)
            .Include(u => u.CaseStudy)
            .FirstOrDefaultAsync(u => u.Slug == slug);

    public async Task<IReadOnlyList<UseCase>> GetByDepartmentIdAsync(int departmentId) =>
        await _context.UseCases
            .Where(u => u.DepartmentId == departmentId)
            .Include(u => u.CaseStudy)
            .OrderBy(u => u.DescriptiveName)
            .ToListAsync();

    public async Task<IReadOnlyList<UseCase>> GetByCaseStudyIdAsync(int caseStudyId) =>
        await _context.UseCases
            .Where(u => u.CaseStudyId == caseStudyId)
            .Include(u => u.Department)
            .OrderBy(u => u.DescriptiveName)
            .ToListAsync();

    public async Task<IReadOnlyList<UseCase>> GetAllAsync() =>
        await _context.UseCases
            .Include(u => u.Department)
            .OrderBy(u => u.Department.Name)
            .ThenBy(u => u.DescriptiveName)
            .ToListAsync();

    public async Task AddAsync(UseCase useCase)
    {
        await _context.UseCases.AddAsync(useCase);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(UseCase useCase)
    {
        _context.UseCases.Update(useCase);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.UseCases.FindAsync(id);
        if (entity is not null)
        {
            _context.UseCases.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}
