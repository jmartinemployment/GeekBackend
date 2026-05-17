using System.Data;
using Dapper;
using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class OAuthTokenRepository : IOAuthTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public OAuthTokenRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<CreateOAuthTokenRequest>> CreateAsync(CreateOAuthTokenRequest request)
    {
        try
        {
            const string sql = """
                INSERT INTO oauth_tokens (id, client_id, token_type, access_token, refresh_token, expires_in, scope, created_at)
                VALUES (@id, @clientId, @tokenType, @accessToken, @refreshToken, @expiresIn, @scope, NOW())
                RETURNING client_id as ClientId, token_type as TokenType, access_token as AccessToken,
                          refresh_token as RefreshToken, expires_in as ExpiresIn, scope as Scope
                """;

            using var conn = _db.CreateConnection();
            var result = await conn.QueryFirstOrDefaultAsync<CreateOAuthTokenRequest>(sql, new
            {
                id = Guid.NewGuid(),
                clientId = request.ClientId,
                tokenType = request.TokenType,
                accessToken = request.AccessToken,
                refreshToken = request.RefreshToken,
                expiresIn = request.ExpiresIn,
                scope = request.Scope
            });

            return result != null
                ? Result<CreateOAuthTokenRequest>.Success(result)
                : Result<CreateOAuthTokenRequest>.Failure("Failed to create token");
        }
        catch (Exception ex)
        {
            return Result<CreateOAuthTokenRequest>.Failure($"Create token failed: {ex.Message}");
        }
    }

    public async Task<Result<CreateOAuthTokenRequest>> FindByAccessTokenAsync(string accessToken)
    {
        try
        {
            const string sql = """
                SELECT client_id as ClientId, token_type as TokenType, access_token as AccessToken,
                       refresh_token as RefreshToken, expires_in as ExpiresIn, scope as Scope
                FROM oauth_tokens
                WHERE access_token = @accessToken
                """;

            using var conn = _db.CreateConnection();
            var result = await conn.QueryFirstOrDefaultAsync<CreateOAuthTokenRequest>(sql, new { accessToken });

            return result != null
                ? Result<CreateOAuthTokenRequest>.Success(result)
                : Result<CreateOAuthTokenRequest>.NotFound("Token not found");
        }
        catch (Exception ex)
        {
            return Result<CreateOAuthTokenRequest>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<CreateOAuthTokenRequest>> FindByRefreshTokenAsync(string refreshToken)
    {
        try
        {
            const string sql = """
                SELECT client_id as ClientId, token_type as TokenType, access_token as AccessToken,
                       refresh_token as RefreshToken, expires_in as ExpiresIn, scope as Scope
                FROM oauth_tokens
                WHERE refresh_token = @refreshToken
                """;

            using var conn = _db.CreateConnection();
            var result = await conn.QueryFirstOrDefaultAsync<CreateOAuthTokenRequest>(sql, new { refreshToken });

            return result != null
                ? Result<CreateOAuthTokenRequest>.Success(result)
                : Result<CreateOAuthTokenRequest>.NotFound("Token not found");
        }
        catch (Exception ex)
        {
            return Result<CreateOAuthTokenRequest>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<CreateOAuthTokenRequest>> UpdateAsync(string tokenId, UpdateOAuthTokenRequest request)
    {
        try
        {
            const string sql = """
                UPDATE oauth_tokens
                SET token_type   = COALESCE(@tokenType, token_type),
                    access_token = COALESCE(@accessToken, access_token),
                    expires_in   = COALESCE(@expiresIn, expires_in),
                    refresh_token = COALESCE(@refreshToken, refresh_token),
                    scope        = COALESCE(@scope, scope)
                WHERE id = @tokenId::uuid
                RETURNING client_id as ClientId, token_type as TokenType, access_token as AccessToken,
                          refresh_token as RefreshToken, expires_in as ExpiresIn, scope as Scope
                """;

            using var conn = _db.CreateConnection();
            var result = await conn.QueryFirstOrDefaultAsync<CreateOAuthTokenRequest>(sql, new
            {
                tokenId,
                tokenType = request.TokenType,
                accessToken = request.AccessToken,
                expiresIn = request.ExpiresIn,
                refreshToken = request.RefreshToken,
                scope = request.Scope
            });

            return result != null
                ? Result<CreateOAuthTokenRequest>.Success(result)
                : Result<CreateOAuthTokenRequest>.NotFound("Token not found");
        }
        catch (Exception ex)
        {
            return Result<CreateOAuthTokenRequest>.Failure($"Update failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(string tokenId)
    {
        try
        {
            const string sql = "DELETE FROM oauth_tokens WHERE id = @tokenId::uuid";
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { tokenId });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Token not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> RevokeTokenAsync(string jti, string reason)
    {
        try
        {
            const string sql = """
                INSERT INTO jti_blacklist (jti, expires_at)
                VALUES (@jti, NOW() + INTERVAL '1 hour')
                ON CONFLICT (jti) DO NOTHING
                RETURNING jti
                """;

            using var conn = _db.CreateConnection();
            var returned = await conn.QueryFirstOrDefaultAsync<string>(sql, new { jti });
            return Result<bool>.Success(returned != null);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Revoke failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IsTokenBlacklistedAsync(string jti)
    {
        try
        {
            const string sql = """
                SELECT 1 FROM jti_blacklist WHERE jti = @jti AND expires_at > NOW()
                """;

            using var conn = _db.CreateConnection();
            var exists = await conn.ExecuteScalarAsync<int?>(sql, new { jti });
            return Result<bool>.Success(exists.HasValue);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> AddToBlacklistAsync(string jti, Guid userId, DateTime expiresAt, string reason)
    {
        try
        {
            const string sql = """
                INSERT INTO jti_blacklist (jti, expires_at)
                VALUES (@jti, @expiresAt)
                ON CONFLICT (jti) DO NOTHING
                RETURNING jti
                """;

            using var conn = _db.CreateConnection();
            var returned = await conn.QueryFirstOrDefaultAsync<string>(sql, new { jti, expiresAt });
            return Result<bool>.Success(returned != null);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Add to blacklist failed: {ex.Message}");
        }
    }

    public async Task<Result<List<string>>> GetUserBlacklistedJtisAsync(Guid userId)
    {
        try
        {
            const string sql = "SELECT jti FROM jti_blacklist WHERE expires_at > NOW()";
            using var conn = _db.CreateConnection();
            var results = await conn.QueryAsync<string>(sql);
            return Result<List<string>>.Success(results.ToList());
        }
        catch (Exception ex)
        {
            return Result<List<string>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> CleanupExpiredBlacklistEntriesAsync()
    {
        try
        {
            const string sql = "DELETE FROM jti_blacklist WHERE expires_at <= NOW()";
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(sql);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Cleanup failed: {ex.Message}");
        }
    }
}
