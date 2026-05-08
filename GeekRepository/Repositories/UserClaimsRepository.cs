using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class UserClaimsRepository : IUserClaimsRepository
{
	private readonly IDbConnectionFactory _db;

	public UserClaimsRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<UserClaim>> CreateAsync(Guid userId, string claimType, string claimValue)
	{
		try
		{
			const string sql = """
				INSERT INTO user_claims (id, user_id, claim_type, claim_value)
				VALUES (@id, @userId, @claimType, @claimValue)
				RETURNING id, user_id as UserId, claim_type as ClaimType, claim_value as ClaimValue,
						  created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var claimId = Guid.NewGuid();
			var claim = await conn.QueryFirstOrDefaultAsync<UserClaim>(sql, new
			{
				id = claimId,
				userId,
				claimType,
				claimValue
			});

			return claim != null
				? Result<UserClaim>.Success(claim)
				: Result<UserClaim>.Failure("Failed to create claim");
		}
		catch (Exception ex)
		{
			return Result<UserClaim>.Failure($"Create claim failed: {ex.Message}");
		}
	}

	public async Task<Result<UserClaim>> FindByIdAsync(Guid claimId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, claim_type as ClaimType,
					   claim_value as ClaimValue, created_at as CreatedAt
				FROM user_claims
				WHERE id = @claimId
				""";

			using var conn = _db.CreateConnection();
			var claim = await conn.QueryFirstOrDefaultAsync<UserClaim>(sql, new { claimId });

			return claim != null
				? Result<UserClaim>.Success(claim)
				: Result<UserClaim>.NotFound("Claim not found");
		}
		catch (Exception ex)
		{
			return Result<UserClaim>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<UserClaim>>> GetByUserIdAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, claim_type as ClaimType,
					   claim_value as ClaimValue, created_at as CreatedAt
				FROM user_claims
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var claims = (await conn.QueryAsync<UserClaim>(sql, new { userId })).ToList();

			return Result<List<UserClaim>>.Success(claims);
		}
		catch (Exception ex)
		{
			return Result<List<UserClaim>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<UserClaim>>> GetByUserIdAndTypeAsync(Guid userId, string claimType)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, claim_type as ClaimType,
					   claim_value as ClaimValue, created_at as CreatedAt
				FROM user_claims
				WHERE user_id = @userId AND claim_type = @claimType
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var claims = (await conn.QueryAsync<UserClaim>(sql, new { userId, claimType })).ToList();

			return Result<List<UserClaim>>.Success(claims);
		}
		catch (Exception ex)
		{
			return Result<List<UserClaim>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DeleteAsync(Guid claimId)
	{
		try
		{
			const string sql = """
				DELETE FROM user_claims
				WHERE id = @claimId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { claimId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Claim not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Delete failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DeleteByUserAsync(Guid userId, string claimType, string claimValue)
	{
		try
		{
			const string sql = """
				DELETE FROM user_claims
				WHERE user_id = @userId AND claim_type = @claimType AND claim_value = @claimValue
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { userId, claimType, claimValue });

			return Result<bool>.Success(rowsAffected > 0);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Delete failed: {ex.Message}");
		}
	}
}
