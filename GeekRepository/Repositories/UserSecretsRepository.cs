using Dapper;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class UserSecretsRepository : IUserSecretsRepository
{
    private readonly IDbConnectionFactory _db;

    public UserSecretsRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<string?>> GetTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                SELECT secret_value FROM user_secrets
                WHERE user_id = @userId AND secret_type = 'totp_secret'
                ORDER BY created_at DESC
                LIMIT 1
                """;

            using var conn = _db.CreateConnection();
            var secret = await conn.QueryFirstOrDefaultAsync<string?>(sql, new { userId });
            return Result<string?>.Success(secret);
        }
        catch (Exception ex)
        {
            return Result<string?>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> SetTotpSecretAsync(Guid userId, string base32Secret, CancellationToken cancellationToken = default)
    {
        try
        {
            const string deleteSql = """
                DELETE FROM user_secrets
                WHERE user_id = @userId AND secret_type = 'totp_secret'
                """;

            const string insertSql = """
                INSERT INTO user_secrets (id, user_id, secret_type, secret_value, created_at)
                VALUES (@id, @userId, 'totp_secret', @secret, CURRENT_TIMESTAMP)
                """;

            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(deleteSql, new { userId });
            await conn.ExecuteAsync(insertSql, new { id = Guid.NewGuid(), userId, secret = base32Secret });
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> ClearTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                DELETE FROM user_secrets
                WHERE user_id = @userId AND secret_type IN ('totp_secret', 'recovery_code')
                """;

            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(sql, new { userId });
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetRecoveryCodeHashesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                SELECT secret_value FROM user_secrets
                WHERE user_id = @userId AND secret_type = 'recovery_code' AND consumed_at IS NULL
                """;

            using var conn = _db.CreateConnection();
            var rows = await conn.QueryAsync<string>(sql, new { userId });
            return Result<IReadOnlyList<string>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> SetRecoveryCodeHashesAsync(Guid userId, IReadOnlyList<string> hashedCodes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM user_secrets WHERE user_id = @userId AND secret_type = 'recovery_code'",
                new { userId });

            const string insertSql = """
                INSERT INTO user_secrets (id, user_id, secret_type, secret_value, created_at)
                VALUES (@id, @userId, 'recovery_code', @hash, CURRENT_TIMESTAMP)
                """;

            foreach (var hash in hashedCodes)
            {
                await conn.ExecuteAsync(insertSql, new { id = Guid.NewGuid(), userId, hash });
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }
}
