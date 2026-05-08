namespace GeekApplication.Services;

public interface IDevicePolicyService
{
    Task<Result<bool>> IsDeviceAllowedAsync(Guid userId, string deviceFingerprint);
    Task<Result<bool>> EnforceSingleDeviceAsync(Guid userId, string newDeviceFingerprint);
    Task<Result<int>> GetActiveDeviceCountAsync(Guid userId);
    Task<Result<bool>> IsAtDeviceLimitAsync(Guid userId, int limit);
    Task<Result<List<DeviceOauth>>> GetDevicesExceedingLimitAsync(Guid userId, int limit);
}
