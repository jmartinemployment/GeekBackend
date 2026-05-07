using GeekBackend.Data.Models;

namespace GeekRepository.Repositories;

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(int id);
    Task<IReadOnlyList<Department>> GetAllAsync();
    Task AddAsync(Department department);
    Task UpdateAsync(Department department);
    Task DeleteAsync(int id);
}
