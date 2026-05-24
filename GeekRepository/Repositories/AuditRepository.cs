using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class AuditRepository : IAuditRepository
{
    private readonly IDbConnectionFactory _db;

    public AuditRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<bool>> CreateLogAsync(AuditLog log)
    {
        try
        {
            const string sql = """
				INSERT INTO audit_log (id, user_id, device_id, event_type, description, ip_address, user_agent, is_success, error_code)
				VALUES (@id, @userId, @deviceId, @eventType, @description, @ipAddress, @userAgent, @isSuccess, @errorCode)
				""";

            using var conn = _db.CreateConnection();
            var logId = Guid.NewGuid();
            var rowsAffected = await conn.ExecuteAsync(sql, new
            {
                id = logId,
                log.UserId,
                log.DeviceId,
                log.EventType,
                log.Description,
                log.IpAddress,
                log.UserAgent,
                log.IsSuccess,
                log.ErrorCode
            });

            return Result<bool>.Success(rowsAffected > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Create log failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> CreateCircuitResetAsync(Guid userId, int failureCount)
    {
        try
        {
            const string sql = """
				INSERT INTO audit_log (id, user_id, event_type, description, is_success)
				VALUES (@id, @userId, 'CIRCUIT_RESET', @description, false)
				""";

            using var conn = _db.CreateConnection();
            var logId = Guid.NewGuid();
            var rowsAffected = await conn.ExecuteAsync(sql, new
            {
                id = logId,
                userId,
                description = $"Circuit breaker reset after {failureCount} failures"
            });

            return Result<bool>.Success(rowsAffected > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Create circuit reset failed: {ex.Message}");
        }
    }

    public async Task<Result<AuditLog>> LogAsync(Guid userId, Guid? deviceId, string eventType, string description, string? ipAddress, string? userAgent, bool isSuccess, string? errorCode)
    {
        try
        {
            const string sql = """
				INSERT INTO audit_log (id, user_id, device_id, event_type, description, ip_address, user_agent, is_success, error_code)
				VALUES (@id, @userId, @deviceId, @eventType, @description, @ipAddress, @userAgent, @isSuccess, @errorCode)
				RETURNING id, user_id as UserId, device_id as DeviceId, event_type as EventType,
						  description, ip_address as IpAddress, user_agent as UserAgent,
						  is_success as IsSuccess, error_code as ErrorCode, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var logId = Guid.NewGuid();
            var log = await conn.QueryFirstOrDefaultAsync<AuditLog>(sql, new
            {
                id = logId,
                userId,
                deviceId,
                eventType,
                description,
                ipAddress,
                userAgent,
                isSuccess,
                errorCode
            });

            return log != null
                ? Result<AuditLog>.Success(log)
                : Result<AuditLog>.Failure("Failed to create log");
        }
        catch (Exception ex)
        {
            return Result<AuditLog>.Failure($"Log failed: {ex.Message}");
        }
    }

    public async Task<Result<List<AuditLog>>> GetUserAuditLogsAsync(Guid userId, int skip = 0, int take = 50)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, event_type as EventType,
					   description, ip_address as IpAddress, user_agent as UserAgent,
					   is_success as IsSuccess, error_code as ErrorCode, created_at as CreatedAt
				FROM audit_log
				WHERE user_id = @userId
				ORDER BY created_at DESC
				OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
				""";

            using var conn = _db.CreateConnection();
            var logs = (await conn.QueryAsync<AuditLog>(sql, new { userId, skip, take })).ToList();

            return Result<List<AuditLog>>.Success(logs);
        }
        catch (Exception ex)
        {
            return Result<List<AuditLog>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<AuditLog>>> GetAuditLogsByEventTypeAsync(string eventType, int skip = 0, int take = 50)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, event_type as EventType,
					   description, ip_address as IpAddress, user_agent as UserAgent,
					   is_success as IsSuccess, error_code as ErrorCode, created_at as CreatedAt
				FROM audit_log
				WHERE event_type = @eventType
				ORDER BY created_at DESC
				OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
				""";

            using var conn = _db.CreateConnection();
            var logs = (await conn.QueryAsync<AuditLog>(sql, new { eventType, skip, take })).ToList();

            return Result<List<AuditLog>>.Success(logs);
        }
        catch (Exception ex)
        {
            return Result<List<AuditLog>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<AuditLog>>> GetRecentFailedLoginsAsync(Guid userId, int minutesBack = 30)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, event_type as EventType,
					   description, ip_address as IpAddress, user_agent as UserAgent,
					   is_success as IsSuccess, error_code as ErrorCode, created_at as CreatedAt
				FROM audit_log
				WHERE user_id = @userId AND event_type = 'LOGIN_FAILED'
					  AND created_at > CURRENT_TIMESTAMP - INTERVAL '1 minute' * @minutesBack
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var logs = (await conn.QueryAsync<AuditLog>(sql, new { userId, minutesBack })).ToList();

            return Result<List<AuditLog>>.Success(logs);
        }
        catch (Exception ex)
        {
            return Result<List<AuditLog>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<SecurityIncident>> LogSecurityIncidentAsync(Guid userId, Guid? deviceId, string incidentType, string description, string? ipAddress, string? deviceFingerprint, string? evidence, bool requiresUserAction)
    {
        try
        {
            const string sql = """
				INSERT INTO security_incidents (id, user_id, device_id, incident_type, description, ip_address, device_fingerprint, evidence, requires_user_action)
				VALUES (@id, @userId, @deviceId, @incidentType, @description, @ipAddress, @deviceFingerprint, @evidence, @requiresUserAction)
				RETURNING id, user_id as UserId, device_id as DeviceId, incident_type as IncidentType,
						  description, ip_address as IpAddress, device_fingerprint as DeviceFingerprint,
						  evidence, requires_user_action as RequiresUserAction, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var incidentId = Guid.NewGuid();
            var incident = await conn.QueryFirstOrDefaultAsync<SecurityIncident>(sql, new
            {
                id = incidentId,
                userId,
                deviceId,
                incidentType,
                description,
                ipAddress,
                deviceFingerprint,
                evidence,
                requiresUserAction
            });

            return incident != null
                ? Result<SecurityIncident>.Success(incident)
                : Result<SecurityIncident>.Failure("Failed to log incident");
        }
        catch (Exception ex)
        {
            return Result<SecurityIncident>.Failure($"Log incident failed: {ex.Message}");
        }
    }

    public async Task<Result<List<SecurityIncident>>> GetUserSecurityIncidentsAsync(Guid userId, bool unresolvedOnly = false)
    {
        try
        {
            var sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, incident_type as IncidentType,
					   description, ip_address as IpAddress, device_fingerprint as DeviceFingerprint,
					   evidence, requires_user_action as RequiresUserAction, created_at as CreatedAt
				FROM security_incidents
				WHERE user_id = @userId
				""";

            if (unresolvedOnly)
                sql += "\nAND resolved_at IS NULL";

            sql += "\nORDER BY created_at DESC";

            using var conn = _db.CreateConnection();
            var incidents = (await conn.QueryAsync<SecurityIncident>(sql, new { userId })).ToList();

            return Result<List<SecurityIncident>>.Success(incidents);
        }
        catch (Exception ex)
        {
            return Result<List<SecurityIncident>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<SecurityIncident?>> GetSecurityIncidentAsync(Guid incidentId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, incident_type as IncidentType,
					   description, ip_address as IpAddress, device_fingerprint as DeviceFingerprint,
					   evidence, requires_user_action as RequiresUserAction, created_at as CreatedAt
				FROM security_incidents
				WHERE id = @incidentId
				""";

            using var conn = _db.CreateConnection();
            var incident = await conn.QueryFirstOrDefaultAsync<SecurityIncident>(sql, new { incidentId });

            return Result<SecurityIncident?>.Success(incident);
        }
        catch (Exception ex)
        {
            return Result<SecurityIncident?>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ResolveSecurityIncidentAsync(Guid incidentId)
    {
        try
        {
            const string sql = """
				UPDATE security_incidents
				SET resolved_at = CURRENT_TIMESTAMP
				WHERE id = @incidentId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { incidentId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Incident not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Resolve failed: {ex.Message}");
        }
    }
}
