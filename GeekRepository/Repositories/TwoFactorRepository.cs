using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class TwoFactorRepository : ITwoFactorRepository
{
	private readonly IDbConnectionFactory _db;

	public TwoFactorRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<TwoFactorPendingSession>> CreatePendingSessionAsync(Guid userId, string secret, DateTime expiresAt)
	{
		try
		{
			const string sql = """
				INSERT INTO two_factor_pending_sessions (id, user_id, secret, expires_at)
				VALUES (@id, @userId, @secret, @expiresAt)
				RETURNING id, user_id as UserId, secret, expires_at as ExpiresAt, created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var sessionId = Guid.NewGuid();
			var session = await conn.QueryFirstOrDefaultAsync<TwoFactorPendingSession>(sql, new
			{
				id = sessionId,
				userId,
				secret,
				expiresAt
			});

			return session != null
				? Result<TwoFactorPendingSession>.Success(session)
				: Result<TwoFactorPendingSession>.Failure("Failed to create session");
		}
		catch (Exception ex)
		{
			return Result<TwoFactorPendingSession>.Failure($"Create session failed: {ex.Message}");
		}
	}

	public async Task<Result<TwoFactorPendingSession>> FindPendingSessionAsync(Guid sessionId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, secret, expires_at as ExpiresAt, created_at as CreatedAt
				FROM two_factor_pending_sessions
				WHERE id = @sessionId
				""";

			using var conn = _db.CreateConnection();
			var session = await conn.QueryFirstOrDefaultAsync<TwoFactorPendingSession>(sql, new { sessionId });

			return session != null
				? Result<TwoFactorPendingSession>.Success(session)
				: Result<TwoFactorPendingSession>.NotFound("Session not found");
		}
		catch (Exception ex)
		{
			return Result<TwoFactorPendingSession>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> CompletePendingSessionAsync(Guid sessionId)
	{
		try
		{
			const string sql = """
				UPDATE two_factor_pending_sessions
				SET is_completed = true, completed_at = CURRENT_TIMESTAMP
				WHERE id = @sessionId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { sessionId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Session not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Complete failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> ExpirePendingSessionAsync(Guid sessionId)
	{
		try
		{
			const string sql = """
				DELETE FROM two_factor_pending_sessions
				WHERE id = @sessionId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { sessionId });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Expire failed: {ex.Message}");
		}
	}

	public async Task<Result<TwoFactorTrustedDevice>> TrustDeviceAsync(Guid userId, Guid deviceId)
	{
		try
		{
			const string sql = """
				INSERT INTO two_factor_trusted_devices (id, user_id, device_id, trusted_at)
				VALUES (@id, @userId, @deviceId, CURRENT_TIMESTAMP)
				RETURNING id, user_id as UserId, device_id as DeviceId, trusted_at as TrustedAt
				""";

			using var conn = _db.CreateConnection();
			var trustId = Guid.NewGuid();
			var trust = await conn.QueryFirstOrDefaultAsync<TwoFactorTrustedDevice>(sql, new
			{
				id = trustId,
				userId,
				deviceId
			});

			return trust != null
				? Result<TwoFactorTrustedDevice>.Success(trust)
				: Result<TwoFactorTrustedDevice>.Failure("Failed to trust device");
		}
		catch (Exception ex)
		{
			return Result<TwoFactorTrustedDevice>.Failure($"Trust device failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> IsDeviceTrustedAsync(Guid userId, Guid deviceId)
	{
		try
		{
			const string sql = """
				SELECT EXISTS(
					SELECT 1 FROM two_factor_trusted_devices
					WHERE user_id = @userId AND device_id = @deviceId
				)
				""";

			using var conn = _db.CreateConnection();
			var isTrusted = await conn.QueryFirstOrDefaultAsync<bool>(sql, new { userId, deviceId });

			return Result<bool>.Success(isTrusted);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<TwoFactorTrustedDevice>>> GetTrustedDevicesAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, trusted_at as TrustedAt
				FROM two_factor_trusted_devices
				WHERE user_id = @userId
				ORDER BY trusted_at DESC
				""";

			using var conn = _db.CreateConnection();
			var devices = (await conn.QueryAsync<TwoFactorTrustedDevice>(sql, new { userId })).ToList();

			return Result<List<TwoFactorTrustedDevice>>.Success(devices);
		}
		catch (Exception ex)
		{
			return Result<List<TwoFactorTrustedDevice>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> RevokeTrustedDeviceAsync(Guid userId, Guid deviceId)
	{
		try
		{
			const string sql = """
				DELETE FROM two_factor_trusted_devices
				WHERE user_id = @userId AND device_id = @deviceId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { userId, deviceId });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Revoke failed: {ex.Message}");
		}
	}
}
