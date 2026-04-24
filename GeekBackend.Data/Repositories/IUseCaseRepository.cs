using System.Collections.Generic;
using System.Threading.Tasks;
using GeekBackend.Data.Models;

namespace GeekBackend.Data.Repositories;

public interface IUseCaseRepository
{
    Task<UseCase?> GetByIdAsync(int id);
    Task<UseCase?> GetBySlugAsync(string slug);
    Task<IReadOnlyList<UseCase>> GetByDepartmentIdAsync(int departmentId);
    Task<IReadOnlyList<UseCase>> GetByCaseStudyIdAsync(int caseStudyId);
    Task<IReadOnlyList<UseCase>> GetAllAsync();
    Task AddAsync(UseCase useCase);
    Task UpdateAsync(UseCase useCase);
    Task DeleteAsync(int id);
}
