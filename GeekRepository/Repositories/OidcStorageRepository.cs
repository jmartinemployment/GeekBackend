using Dapper;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public class OidcStorageRepository : IOidcStorageRepository
{
    private readonly AppDbContext _context;

    public OidcStorageRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OidcStorage>> FindAsync(string id)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, kind, payload, expires_at, user_code, token_hash, uid, grant_id, created_at
                FROM geek_auth.oidc_storage
                WHERE id = @Id
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OidcStorage>(
                sql,
                new { Id = id }
            );

            return result != null ? Result<OidcStorage>.Success(result) : Result<OidcStorage>.NotFound("OidcStorage not found");
        }
        catch (Exception ex)
        {
            return Result<OidcStorage>.Failure(ex.Message);
        }
    }

    public async Task<Result<OidcStorage>> FindByUidAsync(string uid)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, kind, payload, expires_at, user_code, token_hash, uid, grant_id, created_at
                FROM geek_auth.oidc_storage
                WHERE uid = @Uid
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OidcStorage>(
                sql,
                new { Uid = uid }
            );

            return result != null ? Result<OidcStorage>.Success(result) : Result<OidcStorage>.NotFound("OidcStorage not found");
        }
        catch (Exception ex)
        {
            return Result<OidcStorage>.Failure(ex.Message);
        }
    }

    public async Task<Result<OidcStorage>> FindByUserCodeAsync(string userCode)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, kind, payload, expires_at, user_code, token_hash, uid, grant_id, created_at
                FROM geek_auth.oidc_storage
                WHERE user_code = @UserCode
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OidcStorage>(
                sql,
                new { UserCode = userCode }
            );

            return result != null ? Result<OidcStorage>.Success(result) : Result<OidcStorage>.NotFound("OidcStorage not found");
        }
        catch (Exception ex)
        {
            return Result<OidcStorage>.Failure(ex.Message);
        }
    }

    public async Task<Result<OidcStorage>> UpsertAsync(string id, string kind, Dictionary<string, object> payload, int? expiresIn)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            var expiresAt = expiresIn.HasValue ? DateTime.UtcNow.AddSeconds(expiresIn.Value) : (DateTime?)null;

            var sql = """
                INSERT INTO geek_auth.oidc_storage (id, kind, payload, expires_at, created_at)
                VALUES (@Id, @Kind, @Payload, @ExpiresAt, NOW())
                ON CONFLICT (id) DO UPDATE SET
                  kind = EXCLUDED.kind,
                  payload = EXCLUDED.payload,
                  expires_at = EXCLUDED.expires_at
                RETURNING id, kind, payload, expires_at, user_code, token_hash, uid, grant_id, created_at
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OidcStorage>(
                sql,
                new { Id = id, Kind = kind, Payload = payloadJson, ExpiresAt = expiresAt }
            );

            return result != null ? Result<OidcStorage>.Success(result) : Result<OidcStorage>.Failure("Upsert failed");
        }
        catch (Exception ex)
        {
            return Result<OidcStorage>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> DestroyAsync(string id)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = "DELETE FROM geek_auth.oidc_storage WHERE id = @Id";

            await connection.ExecuteAsync(sql, new { Id = id });

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> ConsumeAsync(string id)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                UPDATE geek_auth.oidc_storage
                SET payload = payload || ('{"consumed":"' || to_char(NOW(), 'YYYY-MM-DD"T"HH24:MI:SS"Z"') || '"}')::jsonb
                WHERE id = @Id
                """;

            await connection.ExecuteAsync(sql, new { Id = id });

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> RevokeByGrantIdAsync(string grantId)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = "DELETE FROM geek_auth.oidc_storage WHERE grant_id = @GrantId";

            await connection.ExecuteAsync(sql, new { GrantId = grantId });

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
