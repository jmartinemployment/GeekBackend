namespace GeekApplication.Services;

public interface IDeviceService
{
    Task<Result<DeviceOauth>> RegisterDeviceAsync(Guid userId, RegisterDeviceOauthRequest request);
    Task<Result<DeviceOauth>> FindByIdAsync(Guid deviceId);
    Task<Result<List<DeviceOauth>>> GetUserDevicesAsync(Guid userId);
    Task<Result<bool>> RevokeDeviceAsync(Guid deviceId);
    Task<Result<bool>> TrustDeviceAsync(Guid deviceId);
    Task<Result<bool>> EnforceSingleDeviceModeAsync(Guid userId);
    Task<Result<bool>> EnforceMaxDeviceCountAsync(Guid userId, int maxDevices);
}
