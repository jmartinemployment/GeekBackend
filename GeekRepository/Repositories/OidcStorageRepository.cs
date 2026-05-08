using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class OidcStorageRepository : IOidcStorageRepository
{
	private readonly IDbConnectionFactory _db;

	public OidcStorageRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<bool>> UpsertAsync(OidcStorage storage)
	{
		try
		{
			const string sql = """
				INSERT INTO oidc_storage (kind, key, value, uid)
				VALUES (@kind, @key, @value, @uid)
				ON CONFLICT (kind, key) DO UPDATE
				SET value = @value, uid = @uid
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new
			{
				storage.Kind,
				storage.Payload,
				storage.Uid
			});

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Upsert failed: {ex.Message}");
		}
	}

	public async Task<Result<OidcStorage?>> FindAsync(string kind, string key)
	{
		try
		{
			const string sql = """
				SELECT kind, key, value, uid, created_at as CreatedAt
				FROM oidc_storage
				WHERE kind = @kind AND key = @key
				""";

			using var conn = _db.CreateConnection();
			var storage = await conn.QueryFirstOrDefaultAsync<OidcStorage>(sql, new { kind, key });

			return Result<OidcStorage?>.Success(storage);
		}
		catch (Exception ex)
		{
			return Result<OidcStorage?>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<OidcStorage?>> FindByUidAsync(string uid)
	{
		try
		{
			const string sql = """
				SELECT kind, key, value, uid, created_at as CreatedAt
				FROM oidc_storage
				WHERE uid = @uid
				""";

			using var conn = _db.CreateConnection();
			var storage = await conn.QueryFirstOrDefaultAsync<OidcStorage>(sql, new { uid });

			return Result<OidcStorage?>.Success(storage);
		}
		catch (Exception ex)
		{
			return Result<OidcStorage?>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<OidcStorage?>> FindByUserCodeAsync(string userCode)
	{
		try
		{
			const string sql = """
				SELECT kind, key, value, uid, created_at as CreatedAt
				FROM oidc_storage
				WHERE value LIKE @userCode
				""";

			using var conn = _db.CreateConnection();
			var storage = await conn.QueryFirstOrDefaultAsync<OidcStorage>(sql, new { userCode = $"%{userCode}%" });

			return Result<OidcStorage?>.Success(storage);
		}
		catch (Exception ex)
		{
			return Result<OidcStorage?>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DestroyAsync(string key)
	{
		try
		{
			const string sql = """
				DELETE FROM oidc_storage
				WHERE key = @key
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { key });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Destroy failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> ConsumeAsync(string key)
	{
		try
		{
			const string sql = """
				UPDATE oidc_storage
				SET consumed = true, consumed_at = CURRENT_TIMESTAMP
				WHERE key = @key
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { key });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Consume failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> RevokeByGrantIdAsync(string grantId)
	{
		try
		{
			const string sql = """
				DELETE FROM oidc_storage
				WHERE value LIKE @grantId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { grantId = $"%{grantId}%" });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Revoke failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> StoreNonceAsync(string nonce, string clientId, DateTime expiresAt)
	{
		try
		{
			const string sql = """
				INSERT INTO oidc_storage (kind, key, value, expires_at)
				VALUES (@kind, @nonce, @clientId, @expiresAt)
				ON CONFLICT (kind, key) DO NOTHING
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new
			{
				kind = "nonce",
				nonce,
				clientId,
				expiresAt
			});

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Store nonce failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> ValidateNonceAsync(string nonce, string clientId)
	{
		try
		{
			const string sql = """
				SELECT EXISTS(
					SELECT 1 FROM oidc_storage
					WHERE kind = 'nonce' AND key = @nonce AND value = @clientId
					AND expires_at > CURRENT_TIMESTAMP
				)
				""";

			using var conn = _db.CreateConnection();
			var isValid = await conn.QueryFirstOrDefaultAsync<bool>(sql, new { nonce, clientId });

			return Result<bool>.Success(isValid);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Validate nonce failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> RevokeNonceAsync(string nonce)
	{
		try
		{
			const string sql = """
				DELETE FROM oidc_storage
				WHERE kind = 'nonce' AND key = @nonce
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { nonce });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Revoke nonce failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> StoreAuthorizationCodeAsync(string code, Guid userId, string clientId, List<string> scopes, DateTime expiresAt)
	{
		try
		{
			const string sql = """
				INSERT INTO oidc_storage (kind, key, value, user_id, scope, expires_at)
				VALUES (@kind, @code, @clientId, @userId, @scope, @expiresAt)
				ON CONFLICT (kind, key) DO NOTHING
				""";

			using var conn = _db.CreateConnection();
			var scope = string.Join(" ", scopes);
			var rowsAffected = await conn.ExecuteAsync(sql, new
			{
				kind = "authorization_code",
				code,
				clientId,
				userId,
				scope,
				expiresAt
			});

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Store authorization code failed: {ex.Message}");
		}
	}

	public async Task<Result<(Guid UserId, string ClientId, List<string> Scopes)?>> ValidateAuthorizationCodeAsync(string code)
	{
		try
		{
			const string sql = """
				SELECT user_id as UserId, value as ClientId, scope
				FROM oidc_storage
				WHERE kind = 'authorization_code' AND key = @code
				AND expires_at > CURRENT_TIMESTAMP AND consumed = false
				""";

			using var conn = _db.CreateConnection();
			dynamic? result = await conn.QueryFirstOrDefaultAsync(sql, new { code });

			if (result == null)
				return Result<(Guid UserId, string ClientId, List<string> Scopes)?>.Success(null);

			var userId = (Guid)result.UserId;
			var clientId = (string)result.ClientId;
			var scopes = ((string?)result.Scope ?? "").Split(' ').ToList();

			return Result<(Guid UserId, string ClientId, List<string> Scopes)?>.Success((userId, clientId, scopes));
		}
		catch (Exception ex)
		{
			return Result<(Guid UserId, string ClientId, List<string> Scopes)?>.Failure($"Validate authorization code failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> RevokeAuthorizationCodeAsync(string code)
	{
		try
		{
			const string sql = """
				DELETE FROM oidc_storage
				WHERE kind = 'authorization_code' AND key = @code
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { code });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Revoke authorization code failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> CleanupExpiredAsync()
	{
		try
		{
			const string sql = """
				DELETE FROM oidc_storage
				WHERE expires_at < CURRENT_TIMESTAMP
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql);

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Cleanup failed: {ex.Message}");
		}
	}
}
