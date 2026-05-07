using Dapper;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class OAuthTokenRepository : IOAuthTokenRepository
{
    private readonly AppDbContext _context;

    public OAuthTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OauthToken>> FindByAccessTokenAsync(string accessToken)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, access_token, access_token_expires_at, refresh_token, refresh_token_expires_at,
                       bios_id, user_id, client_id, scope, is_active, created_at
                FROM geek_auth.oauth_token
                WHERE access_token = @AccessToken
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OauthToken>(
                sql,
                new { AccessToken = accessToken }
            );

            return result != null
                ? Result<OauthToken>.Success(result)
                : Result<OauthToken>.NotFound("OAuthToken not found");
        }
        catch (Exception ex)
        {
            return Result<OauthToken>.Failure(ex.Message);
        }
    }

    public async Task<Result<OauthToken>> FindByRefreshTokenAsync(string refreshToken)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, access_token, access_token_expires_at, refresh_token, refresh_token_expires_at,
                       bios_id, user_id, client_id, scope, is_active, created_at
                FROM geek_auth.oauth_token
                WHERE refresh_token = @RefreshToken
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OauthToken>(
                sql,
                new { RefreshToken = refreshToken }
            );

            return result != null
                ? Result<OauthToken>.Success(result)
                : Result<OauthToken>.NotFound("OAuthToken not found");
        }
        catch (Exception ex)
        {
            return Result<OauthToken>.Failure(ex.Message);
        }
    }

    public async Task<Result<OauthToken>> CreateAsync(CreateOAuthTokenRequest request)
    {
        try
        {
            var token = new OauthToken
            {
                Id = Guid.NewGuid().ToString(),
                AccessToken = request.AccessToken,
                AccessTokenExpiresAt = request.AccessTokenExpiresAt,
                RefreshToken = request.RefreshToken,
                RefreshTokenExpiresAt = request.RefreshTokenExpiresAt,
                BiosId = request.BiosId,
                UserId = request.UserId,
                ClientId = request.ClientId,
                Scope = request.Scope,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.OAuthTokens.Add(token);
            await _context.SaveChangesAsync();

            return Result<OauthToken>.Success(token);
        }
        catch (Exception ex)
        {
            return Result<OauthToken>.Failure(ex.Message);
        }
    }

    public async Task<Result<OauthToken>> UpdateAsync(string id, UpdateOAuthTokenRequest request)
    {
        try
        {
            var token = await _context.OAuthTokens.FirstOrDefaultAsync(t => t.Id == id);
            if (token == null) return Result<OauthToken>.NotFound("OAuthToken not found");

            if (request.IsActive.HasValue) token.IsActive = request.IsActive.Value;
            if (request.AccessTokenExpiresAt.HasValue) token.AccessTokenExpiresAt = request.AccessTokenExpiresAt.Value;
            if (request.RefreshTokenExpiresAt.HasValue) token.RefreshTokenExpiresAt = request.RefreshTokenExpiresAt.Value;

            await _context.SaveChangesAsync();

            return Result<OauthToken>.Success(token);
        }
        catch (Exception ex)
        {
            return Result<OauthToken>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> DeleteAsync(string id)
    {
        try
        {
            var token = await _context.OAuthTokens.FirstOrDefaultAsync(t => t.Id == id);
            if (token == null) return Result<Unit>.NotFound("OAuthToken not found");

            _context.OAuthTokens.Remove(token);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
