using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class DepartmentRepository : IDepartmentRepository
{
    private readonly AppDbContext _context;

    public DepartmentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Department?> GetByIdAsync(int id) =>
        await _context.Departments.FindAsync(id);

    public async Task<IReadOnlyList<Department>> GetAllAsync() =>
        await _context.Departments
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToListAsync();

    public async Task AddAsync(Department department)
    {
        await _context.Departments.AddAsync(department);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Department department)
    {
        _context.Departments.Update(department);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.Departments.FindAsync(id);
        if (entity is not null)
        {
            _context.Departments.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}
