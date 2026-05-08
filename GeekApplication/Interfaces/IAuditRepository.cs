namespace GeekApplication.Interfaces;

public interface IAuditRepository
{
    Task<Result<bool>> CreateLogAsync(AuditLog log);
    Task<Result<bool>> CreateCircuitResetAsync(Guid userId, int failureCount);

    Task<Result<AuditLog>> LogAsync(Guid userId, Guid? deviceId, string eventType, string description, string? ipAddress, string? userAgent, bool isSuccess, string? errorCode);
    Task<Result<List<AuditLog>>> GetUserAuditLogsAsync(Guid userId, int skip = 0, int take = 50);
    Task<Result<List<AuditLog>>> GetAuditLogsByEventTypeAsync(string eventType, int skip = 0, int take = 50);
    Task<Result<List<AuditLog>>> GetRecentFailedLoginsAsync(Guid userId, int minutesBack = 30);

    Task<Result<SecurityIncident>> LogSecurityIncidentAsync(Guid userId, Guid? deviceId, string incidentType, string description, string? ipAddress, string? deviceFingerprint, string? evidence, bool requiresUserAction);
    Task<Result<List<SecurityIncident>>> GetUserSecurityIncidentsAsync(Guid userId, bool unresolvedOnly = false);
    Task<Result<SecurityIncident?>> GetSecurityIncidentAsync(Guid incidentId);
    Task<Result<bool>> ResolveSecurityIncidentAsync(Guid incidentId);
}
