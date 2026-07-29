using GeekApplication.Models.ContentWriterV4;

namespace GeekApplication.Interfaces.ContentWriterV4;

public interface IGenerationRepository
{
    Task<GenerationDto> CreateAsync(CreateGenerationCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<GenerationDto>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    Task<UsageSummaryDto> GetUsageSummaryAsync(CancellationToken ct = default);
}
