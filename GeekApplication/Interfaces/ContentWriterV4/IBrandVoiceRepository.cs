using GeekApplication.Models.ContentWriterV4;

namespace GeekApplication.Interfaces.ContentWriterV4;

public interface IBrandVoiceRepository
{
    Task<BrandVoiceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BrandVoiceDto>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default);
    Task<BrandVoiceDto> CreateAsync(CreateBrandVoiceCommand command, CancellationToken ct = default);
    Task<BrandVoiceDto> UpdateAsync(UpdateBrandVoiceCommand command, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
