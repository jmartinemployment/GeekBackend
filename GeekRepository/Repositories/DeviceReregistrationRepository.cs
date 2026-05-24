using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class DeviceReregistrationRepository : IDeviceReregistrationRepository
{
    private readonly IDbConnectionFactory _db;

    public DeviceReregistrationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<DeviceReregistrationRequest>> CreateAsync(Guid userId, Guid deviceId, string biosId, string machineId, string platform)
    {
        try
        {
            const string sql = """
				INSERT INTO device_reregistration_requests (id, user_id, device_id, bios_id, machine_id, platform, status)
				VALUES (@id, @userId, @deviceId, @biosId, @machineId, @platform, 'pending')
				RETURNING id, user_id as UserId, device_id as DeviceId, bios_id as BiosId,
						  machine_id as MachineId, platform, status, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var requestId = Guid.NewGuid();
            var request = await conn.QueryFirstOrDefaultAsync<DeviceReregistrationRequest>(sql, new
            {
                id = requestId,
                userId,
                deviceId,
                biosId,
                machineId,
                platform
            });

            return request != null
                ? Result<DeviceReregistrationRequest>.Success(request)
                : Result<DeviceReregistrationRequest>.Failure("Failed to create request");
        }
        catch (Exception ex)
        {
            return Result<DeviceReregistrationRequest>.Failure($"Create request failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceReregistrationRequest>> FindByIdAsync(Guid requestId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, bios_id as BiosId,
					   machine_id as MachineId, platform, status, created_at as CreatedAt
				FROM device_reregistration_requests
				WHERE id = @requestId
				""";

            using var conn = _db.CreateConnection();
            var request = await conn.QueryFirstOrDefaultAsync<DeviceReregistrationRequest>(sql, new { requestId });

            return request != null
                ? Result<DeviceReregistrationRequest>.Success(request)
                : Result<DeviceReregistrationRequest>.NotFound("Request not found");
        }
        catch (Exception ex)
        {
            return Result<DeviceReregistrationRequest>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceReregistrationRequest>> FindPendingAsync(Guid userId, Guid deviceId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, bios_id as BiosId,
					   machine_id as MachineId, platform, status, created_at as CreatedAt
				FROM device_reregistration_requests
				WHERE user_id = @userId AND device_id = @deviceId AND status = 'pending'
				ORDER BY created_at DESC
				LIMIT 1
				""";

            using var conn = _db.CreateConnection();
            var request = await conn.QueryFirstOrDefaultAsync<DeviceReregistrationRequest>(sql, new { userId, deviceId });

            return request != null
                ? Result<DeviceReregistrationRequest>.Success(request)
                : Result<DeviceReregistrationRequest>.NotFound("No pending request found");
        }
        catch (Exception ex)
        {
            return Result<DeviceReregistrationRequest>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<DeviceReregistrationRequest>>> GetPendingByUserAsync(Guid userId)
    {
        try
        {
            const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, bios_id as BiosId,
					   machine_id as MachineId, platform, status, created_at as CreatedAt
				FROM device_reregistration_requests
				WHERE user_id = @userId AND status = 'pending'
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var requests = (await conn.QueryAsync<DeviceReregistrationRequest>(sql, new { userId })).ToList();

            return Result<List<DeviceReregistrationRequest>>.Success(requests);
        }
        catch (Exception ex)
        {
            return Result<List<DeviceReregistrationRequest>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ApproveAsync(Guid requestId)
    {
        try
        {
            const string sql = """
				UPDATE device_reregistration_requests
				SET status = 'approved', updated_at = CURRENT_TIMESTAMP
				WHERE id = @requestId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { requestId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Request not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Approve failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> RejectAsync(Guid requestId)
    {
        try
        {
            const string sql = """
				UPDATE device_reregistration_requests
				SET status = 'rejected', updated_at = CURRENT_TIMESTAMP
				WHERE id = @requestId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { requestId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Request not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Reject failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ExpireAsync(Guid requestId)
    {
        try
        {
            const string sql = """
				UPDATE device_reregistration_requests
				SET status = 'expired', updated_at = CURRENT_TIMESTAMP
				WHERE id = @requestId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { requestId });

            return Result<bool>.Success(rowsAffected > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Expire failed: {ex.Message}");
        }
    }
}
