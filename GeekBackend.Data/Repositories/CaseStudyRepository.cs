using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Data.Repositories;

public class CaseStudyRepository : ICaseStudyRepository
{
    private readonly AppDbContext _context;

    public CaseStudyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CaseStudy?> GetByIdAsync(int id) =>
        await _context.CaseStudies
            .Include(c => c.CaseStudyActors.OrderBy(a => a.SortOrder))
            .Include(c => c.CaseStudyMetrics.OrderBy(m => m.SortOrder))
            .Include(c => c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber))
                .ThenInclude(s => s.StepActor)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<CaseStudy?> GetBySlugAsync(string slug) =>
        await _context.CaseStudies
            .Include(c => c.CaseStudyActors.OrderBy(a => a.SortOrder))
            .Include(c => c.CaseStudyMetrics.OrderBy(m => m.SortOrder))
            .Include(c => c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber))
                .ThenInclude(s => s.StepActor)
            .FirstOrDefaultAsync(c => c.Slug == slug);

    public async Task<IReadOnlyList<CaseStudy>> GetAllAsync() =>
        await _context.CaseStudies
            .OrderBy(c => c.DescriptiveName)
            .ToListAsync();

    public async Task<IReadOnlyList<CaseStudy>> GetPublishedAsync() =>
        await _context.CaseStudies
            .Where(c => c.Status == "published")
            .OrderByDescending(c => c.PublishedAt)
            .ToListAsync();

    public async Task AddAsync(CaseStudy caseStudy)
    {
        await _context.CaseStudies.AddAsync(caseStudy);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(CaseStudy caseStudy)
    {
        _context.CaseStudies.Update(caseStudy);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.CaseStudies.FindAsync(id);
        if (entity is not null)
        {
            _context.CaseStudies.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}
