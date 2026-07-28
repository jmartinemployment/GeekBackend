using GeekApplication.Models.ContentWriterV3;

namespace GeekApplication.Interfaces.ContentWriterV3;

public interface IJobRepository
{
    Task<JobDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<JobDto>> GetByStatusAsync(string status, int limit = 100, CancellationToken ct = default);
    Task<JobDto?> GetForProcessingAsync(string jobType, CancellationToken ct = default);
    Task<JobDto> CreateAsync(CreateJobCommand command, CancellationToken ct = default);
    Task<JobDto> UpdateStatusAsync(UpdateJobStatusCommand command, CancellationToken ct = default);
    Task<JobDto> LeaseAsync(Guid id, string leaseOwner, TimeSpan duration, CancellationToken ct = default);
    Task<JobDto> ReleaseLeaseAsync(Guid id, CancellationToken ct = default);
}
