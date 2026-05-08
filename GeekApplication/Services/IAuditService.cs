namespace GeekApplication.Services;

public interface IAuditService
{
    Task<Result<bool>> LogLoginAsync(Guid userId, Guid? deviceId, string? ipAddress, string? userAgent, bool success, string? errorCode);
    Task<Result<bool>> LogLogoutAsync(Guid userId, Guid? deviceId, string? ipAddress);
    Task<Result<bool>> LogPasswordChangeAsync(Guid userId, string? ipAddress);
    Task<Result<bool>> LogDeviceRegisteredAsync(Guid userId, Guid deviceId, string? ipAddress);
    Task<Result<bool>> LogDeviceRevokedAsync(Guid userId, Guid deviceId, string? reason);
    Task<Result<bool>> LogSecurityEventAsync(Guid userId, Guid? deviceId, string eventType, string description, string? ipAddress, string? evidence);
    Task<Result<List<AuditLog>>> GetUserAuditTrailAsync(Guid userId, int days = 90);
    Task<Result<List<SecurityIncident>>> GetSecurityIncidentsAsync(Guid userId);
}
