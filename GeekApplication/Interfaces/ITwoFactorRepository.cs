namespace GeekApplication.Interfaces;

public interface ITwoFactorRepository
{
    Task<Result<TwoFactorPendingSession>> CreatePendingSessionAsync(Guid userId, string secret, DateTime expiresAt);
    Task<Result<TwoFactorPendingSession>> FindPendingSessionAsync(Guid sessionId);
    Task<Result<bool>> CompletePendingSessionAsync(Guid sessionId);
    Task<Result<bool>> ExpirePendingSessionAsync(Guid sessionId);
    Task<Result<TwoFactorTrustedDevice>> TrustDeviceAsync(Guid userId, Guid deviceId);
    Task<Result<bool>> IsDeviceTrustedAsync(Guid userId, Guid deviceId);
    Task<Result<List<TwoFactorTrustedDevice>>> GetTrustedDevicesAsync(Guid userId);
    Task<Result<bool>> RevokeTrustedDeviceAsync(Guid userId, Guid deviceId);
}
