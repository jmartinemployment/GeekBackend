using System.Collections.Generic;
using System.Threading.Tasks;
using GeekBackend.Data.Models;

namespace GeekBackend.Data.Repositories;

public interface ICaseStudyRepository
{
    Task<CaseStudy?> GetByIdAsync(int id);
    Task<CaseStudy?> GetBySlugAsync(string slug);
    Task<IReadOnlyList<CaseStudy>> GetAllAsync();
    Task<IReadOnlyList<CaseStudy>> GetPublishedAsync();
    Task AddAsync(CaseStudy caseStudy);
    Task UpdateAsync(CaseStudy caseStudy);
    Task DeleteAsync(int id);
}
