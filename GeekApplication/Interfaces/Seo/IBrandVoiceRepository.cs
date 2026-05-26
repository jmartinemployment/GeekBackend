using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IBrandVoiceRepository
{
    Task<Result<IReadOnlyList<SeoBrandVoice>>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<Result<SeoBrandVoice>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<SeoBrandVoice>> CreateAsync(Guid userId, CreateBrandVoiceRequest request, CancellationToken ct = default);
    Task<Result<SeoBrandVoice>> UpdateAsync(Guid userId, Guid id, UpdateBrandVoiceRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
