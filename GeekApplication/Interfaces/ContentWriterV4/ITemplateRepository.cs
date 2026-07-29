using GeekApplication.Models.ContentWriterV4;

namespace GeekApplication.Interfaces.ContentWriterV4;

public interface ITemplateRepository
{
    Task<TemplateDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TemplateDto?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<TemplateDto>> GetAllActiveAsync(CancellationToken ct = default);
}
