using GeekApplication.Dtos;

namespace GeekApplication.Interfaces;

public interface IDeviceOauthRepository
{
    Task<Result<DeviceOauth>> RegisterAsync(Guid userId, RegisterDeviceOauthRequest request);
    Task<Result<DeviceOauth>> FindByIdAsync(Guid deviceId);
    Task<Result<DeviceOauth>> FindByFingerprintAsync(Guid userId, string fingerprint);
    Task<Result<List<DeviceOauth>>> GetUserDevicesAsync(Guid userId);
    Task<Result<List<DeviceOauth>>> FindByUserIdAsync(Guid userId);
    Task<Result<DeviceOauth>> UpdateAsync(DeviceOauth device);
    Task<Result<DeviceOauth>> UpsertAsync(Guid userId, UpsertDeviceRequest request);
    Task<Result<bool>> RevokeAsync(Guid deviceId);
    Task<Result<bool>> DeleteAsync(Guid deviceId);
    Task<Result<bool>> TrustAsync(Guid deviceId, int trustDaysOrNull = 30);
    Task<Result<List<DeviceOauth>>> GetActiveDevicesAsync(Guid userId);
    Task<Result<string>> IssueChallengeAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task<Result<bool>> VerifyChallengeAsync(
        Guid deviceId,
        string nonce,
        string signatureBase64,
        string? publicKeyPem,
        CancellationToken cancellationToken = default);
}

public record RegisterDeviceOauthRequest(
    string DeviceType,
    string? DeviceName,
    string BiosId,
    string DeviceFingerprint,
    string Platform
);
