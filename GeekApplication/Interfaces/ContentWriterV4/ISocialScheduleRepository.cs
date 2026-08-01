using GeekApplication.Models.ContentWriterV4;

namespace GeekApplication.Interfaces.ContentWriterV4;

public interface ISocialScheduleRepository
{
    Task<SocialScheduleEntryDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SocialScheduleEntryDto>> GetByOwnerIdAsync(
        Guid ownerId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        Guid? campaignId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<SocialScheduleEntryDto>> GetByCampaignIdAsync(
        Guid campaignId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken ct = default);
    Task<SocialScheduleEntryDto> CreateAsync(CreateSocialScheduleEntryCommand command, CancellationToken ct = default);
    Task<SocialScheduleEntryDto> UpdateAsync(UpdateSocialScheduleEntryCommand command, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
