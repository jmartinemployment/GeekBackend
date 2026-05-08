using System.Collections.Generic;
using System.Threading.Tasks;
using GeekApplication.Entities;

namespace GeekApplication.Interfaces;

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
