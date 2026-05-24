using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class SecurityIncidentRepository : ISecurityIncidentRepository
{
    private readonly IDbConnectionFactory _db;

    public SecurityIncidentRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<SecurityIncident>> CreateAsync(Guid userId, string incidentType, string description, string? metadata)
    {
        try
        {
            const string sql = """
				INSERT INTO security_incidents (id, user_id, incident_type, description, metadata)
				VALUES (@id, @userId, @incidentType, @description, @metadata)
				RETURNING id, user_id as UserId, incident_type as IncidentType,
						  description, metadata, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var incidentId = Guid.NewGuid();
            var incident = await conn.QueryFirstOrDefaultAsync<SecurityIncident>(sql, new
            {
                id = incidentId,
                userId,
                incidentType,
                description,
                metadata
            });

            return incident != null
                ? Result<SecurityIncident>.Success(incident)
                : Result<SecurityIncident>.Failure("Failed to create incident");
        }
        catch (Exception ex)
        {
            return Result<SecurityIncident>.Failure($"Create incident failed: {ex.Message}");
        }
    }

    public async Task<Result<SecurityIncident>> FindByIdAsync(Guid incidentId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, incident_type as IncidentType,
					   description, metadata, created_at as CreatedAt
				FROM security_incidents
				WHERE id = @incidentId
				""";

            using var conn = _db.CreateConnection();
            var incident = await conn.QueryFirstOrDefaultAsync<SecurityIncident>(sql, new { incidentId });

            return incident != null
                ? Result<SecurityIncident>.Success(incident)
                : Result<SecurityIncident>.NotFound("Incident not found");
        }
        catch (Exception ex)
        {
            return Result<SecurityIncident>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<SecurityIncident>>> GetByUserAsync(Guid userId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, incident_type as IncidentType,
					   description, metadata, created_at as CreatedAt
				FROM security_incidents
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var incidents = (await conn.QueryAsync<SecurityIncident>(sql, new { userId })).ToList();

            return Result<List<SecurityIncident>>.Success(incidents);
        }
        catch (Exception ex)
        {
            return Result<List<SecurityIncident>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<SecurityIncident>>> GetByTypeAsync(string incidentType)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, incident_type as IncidentType,
					   description, metadata, created_at as CreatedAt
				FROM security_incidents
				WHERE incident_type = @incidentType
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var incidents = (await conn.QueryAsync<SecurityIncident>(sql, new { incidentType })).ToList();

            return Result<List<SecurityIncident>>.Success(incidents);
        }
        catch (Exception ex)
        {
            return Result<List<SecurityIncident>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<SecurityIncident>>> GetUnresolvedAsync()
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, incident_type as IncidentType,
					   description, metadata, created_at as CreatedAt
				FROM security_incidents
				WHERE resolved_at IS NULL
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var incidents = (await conn.QueryAsync<SecurityIncident>(sql)).ToList();

            return Result<List<SecurityIncident>>.Success(incidents);
        }
        catch (Exception ex)
        {
            return Result<List<SecurityIncident>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> MarkResolvedAsync(Guid incidentId, string resolution)
    {
        try
        {
            const string sql = """
				UPDATE security_incidents
				SET resolved_at = CURRENT_TIMESTAMP, resolution = @resolution
				WHERE id = @incidentId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { incidentId, resolution });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Incident not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Mark resolved failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(Guid incidentId)
    {
        try
        {
            const string sql = """
				DELETE FROM security_incidents
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
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }
}
