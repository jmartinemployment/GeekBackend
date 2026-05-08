using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.JtiBlacklist;

public sealed class PostgresJtiBlacklistRepository : IJtiBlacklist
{
	private readonly IDbConnectionFactory _db;

	public PostgresJtiBlacklistRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<GeekApplication.Entities.JtiBlacklist>> AddAsync(string jti, DateTime expiresAt)
	{
		try
		{
			const string sql = """
				INSERT INTO jti_blacklist (jti, expires_at, blacklisted_at)
				VALUES (@jti, @expiresAt, CURRENT_TIMESTAMP)
				ON CONFLICT (jti) DO NOTHING
				RETURNING jti, user_id as UserId, expires_at as ExpiresAt,
						  blacklisted_at as BlacklistedAt, reason
				""";

			using var conn = _db.CreateConnection();
			var entry = await conn.QueryFirstOrDefaultAsync<GeekApplication.Entities.JtiBlacklist>(sql, new { jti, expiresAt });

			return entry != null
				? Result<GeekApplication.Entities.JtiBlacklist>.Success(entry)
				: Result<GeekApplication.Entities.JtiBlacklist>.Failure("Failed to add jti to blacklist");
		}
		catch (Exception ex)
		{
			return Result<GeekApplication.Entities.JtiBlacklist>.Failure($"Add jti failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> IsRevokedAsync(string jti)
	{
		try
		{
			const string sql = """
				SELECT EXISTS(
					SELECT 1 FROM jti_blacklist
					WHERE jti = @jti AND expires_at > CURRENT_TIMESTAMP
				)
				""";

			using var conn = _db.CreateConnection();
			var isRevoked = await conn.QueryFirstOrDefaultAsync<bool>(sql, new { jti });

			return Result<bool>.Success(isRevoked);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Check revoked failed: {ex.Message}");
		}
	}

	public async Task<Result<GeekApplication.Entities.JtiBlacklist>> FindByJtiAsync(string jti)
	{
		try
		{
			const string sql = """
				SELECT jti, user_id as UserId, expires_at as ExpiresAt,
					   blacklisted_at as BlacklistedAt, reason
				FROM jti_blacklist
				WHERE jti = @jti
				""";

			using var conn = _db.CreateConnection();
			var entry = await conn.QueryFirstOrDefaultAsync<GeekApplication.Entities.JtiBlacklist>(sql, new { jti });

			return entry != null
				? Result<GeekApplication.Entities.JtiBlacklist>.Success(entry)
				: Result<GeekApplication.Entities.JtiBlacklist>.NotFound("JTI not found");
		}
		catch (Exception ex)
		{
			return Result<GeekApplication.Entities.JtiBlacklist>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DeleteAsync(string jti)
	{
		try
		{
			const string sql = """
				DELETE FROM jti_blacklist
				WHERE jti = @jti
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { jti });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("JTI not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Delete failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> CleanupExpiredAsync()
	{
		try
		{
			const string sql = """
				DELETE FROM jti_blacklist
				WHERE expires_at < CURRENT_TIMESTAMP
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql);

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Cleanup expired failed: {ex.Message}");
		}
	}
}
