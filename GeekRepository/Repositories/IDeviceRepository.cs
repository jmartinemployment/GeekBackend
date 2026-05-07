using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IDeviceRepository
{
    Task<Result<Device>> FindByIdAsync(string id);
    Task<Result<Device>> FindByUserAndDeviceIdAsync(string userId, string deviceId);
    Task<Result<List<Device>>> FindByUserIdAsync(string userId);
    Task<Result<Device>> UpsertAsync(UpsertDeviceRequest request);
    Task<Result<Device>> UpdateAsync(string id, UpdateDeviceRequest request);
    Task<Result<Unit>> DeleteAsync(string id);
}

public class UpsertDeviceRequest
{
    public string UserId { get; set; } = null!;
    public string DeviceId { get; set; } = null!;
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}

public class UpdateDeviceRequest
{
    public string? Status { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
    public DateTime? MfaExpiresAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
