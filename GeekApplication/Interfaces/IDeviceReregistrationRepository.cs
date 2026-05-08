namespace GeekApplication.Interfaces;

public interface IDeviceReregistrationRepository
{
    Task<Result<DeviceReregistrationRequest>> CreateAsync(Guid userId, Guid deviceId, string biosId, string machineId, string platform);
    Task<Result<DeviceReregistrationRequest>> FindByIdAsync(Guid requestId);
    Task<Result<DeviceReregistrationRequest>> FindPendingAsync(Guid userId, Guid deviceId);
    Task<Result<List<DeviceReregistrationRequest>>> GetPendingByUserAsync(Guid userId);
    Task<Result<bool>> ApproveAsync(Guid requestId);
    Task<Result<bool>> RejectAsync(Guid requestId);
    Task<Result<bool>> ExpireAsync(Guid requestId);
}
