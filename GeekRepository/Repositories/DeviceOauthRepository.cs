using System.Data;
using Dapper;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class DeviceOauthRepository : IDeviceOauthRepository
{
	private readonly IDbConnectionFactory _db;

	public DeviceOauthRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<DeviceOauth>> RegisterAsync(Guid userId, RegisterDeviceOauthRequest request)
	{
		try
		{
			const string sql = """
				INSERT INTO devices_oauth (id, user_id, device_name, device_type, platform, bios_id, device_fingerprint, is_active)
				VALUES (@id, @userId, @deviceName, @deviceType, @platform, @biosId, @deviceFingerprint, true)
				RETURNING id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
						  platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
						  is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				""";

			using var conn = _db.CreateConnection();
			var deviceId = Guid.NewGuid();
			var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
			{
				id = deviceId,
				userId,
				deviceName = request.DeviceName,
				deviceType = request.DeviceType,
				platform = request.Platform,
				biosId = request.BiosId,
				deviceFingerprint = request.DeviceFingerprint
			});

			return device != null
				? Result<DeviceOauth>.Success(device)
				: Result<DeviceOauth>.Failure("Failed to register device");
		}
		catch (Exception ex)
		{
			return Result<DeviceOauth>.Failure($"Register device failed: {ex.Message}");
		}
	}

	public async Task<Result<DeviceOauth>> FindByIdAsync(Guid deviceId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
					   platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
					   is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				FROM devices_oauth
				WHERE id = @deviceId
				""";

			using var conn = _db.CreateConnection();
			var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new { deviceId });

			return device != null
				? Result<DeviceOauth>.Success(device)
				: Result<DeviceOauth>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<DeviceOauth>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<DeviceOauth>> FindByFingerprintAsync(Guid userId, string fingerprint)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
					   platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
					   is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				FROM devices_oauth
				WHERE user_id = @userId AND device_fingerprint = @fingerprint
				""";

			using var conn = _db.CreateConnection();
			var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new { userId, fingerprint });

			return device != null
				? Result<DeviceOauth>.Success(device)
				: Result<DeviceOauth>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<DeviceOauth>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<DeviceOauth>>> GetUserDevicesAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
					   platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
					   is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				FROM devices_oauth
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

			return Result<List<DeviceOauth>>.Success(devices);
		}
		catch (Exception ex)
		{
			return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<DeviceOauth>>> FindByUserIdAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
					   platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
					   is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				FROM devices_oauth
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

			return Result<List<DeviceOauth>>.Success(devices);
		}
		catch (Exception ex)
		{
			return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<DeviceOauth>> UpdateAsync(DeviceOauth device)
	{
		try
		{
			const string sql = """
				UPDATE devices_oauth
				SET device_name = @deviceName, device_type = @deviceType, platform = @platform,
					bios_id = @biosId, device_fingerprint = @deviceFingerprint, is_active = @isActive,
					updated_at = CURRENT_TIMESTAMP
				WHERE id = @id
				RETURNING id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
						  platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
						  is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				""";

			using var conn = _db.CreateConnection();
			var updated = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
			{
				device.Id,
				device.DeviceName,
				device.DeviceType,
				device.Platform,
				device.BiosId,
				device.DeviceFingerprint,
				device.IsTrusted
			});

			return updated != null
				? Result<DeviceOauth>.Success(updated)
				: Result<DeviceOauth>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<DeviceOauth>.Failure($"Update failed: {ex.Message}");
		}
	}

	public async Task<Result<DeviceOauth>> UpsertAsync(Guid userId, UpsertDeviceRequest request)
	{
		try
		{
			const string sql = """
				INSERT INTO devices_oauth (id, user_id, device_name, device_type, platform, bios_id, device_fingerprint, is_active)
				VALUES (@id, @userId, @deviceName, @deviceType, @platform, @biosId, @deviceFingerprint, true)
				ON CONFLICT (user_id, device_fingerprint) DO UPDATE
				SET device_name = @deviceName, device_type = @deviceType, platform = @platform,
					updated_at = CURRENT_TIMESTAMP, is_active = true
				RETURNING id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
						  platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
						  is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				""";

			using var conn = _db.CreateConnection();
			var deviceId = Guid.NewGuid();
			var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
			{
				id = deviceId,
				userId,
				deviceName = request.DeviceName,
				deviceType = request.DeviceType,
				platform = request.Platform,
				biosId = request.BiosId,
				deviceFingerprint = request.DeviceFingerprint
			});

			return device != null
				? Result<DeviceOauth>.Success(device)
				: Result<DeviceOauth>.Failure("Failed to upsert device");
		}
		catch (Exception ex)
		{
			return Result<DeviceOauth>.Failure($"Upsert failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> RevokeAsync(Guid deviceId)
	{
		try
		{
			const string sql = """
				UPDATE devices_oauth
				SET is_active = false, updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Revoke failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DeleteAsync(Guid deviceId)
	{
		try
		{
			const string sql = """
				DELETE FROM devices_oauth
				WHERE id = @deviceId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Delete failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> TrustAsync(Guid deviceId, int trustDaysOrNull = 30)
	{
		try
		{
			const string sql = """
				UPDATE devices_oauth
				SET trusted_until = CURRENT_TIMESTAMP + INTERVAL '1 day' * @trustDays,
					updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId, trustDays = trustDaysOrNull });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Device not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Trust failed: {ex.Message}");
		}
	}

	public async Task<Result<List<DeviceOauth>>> GetActiveDevicesAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
					   platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
					   is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				FROM devices_oauth
				WHERE user_id = @userId AND is_active = true
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

			return Result<List<DeviceOauth>>.Success(devices);
		}
		catch (Exception ex)
		{
			return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
		}
	}
}
