using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class PendingVerificationRepository : IPendingVerificationRepository
{
    private readonly IDbConnectionFactory _db;

    public PendingVerificationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<bool>> UpsertAsync(string verificationCode, DateTime expiresAt)
    {
        try
        {
            const string sql = """
                INSERT INTO pending_verifications (verification_code, expires_at, attempt_count)
                VALUES (@verificationCode, @expiresAt, 0)
                ON CONFLICT (verification_code)
                DO UPDATE SET expires_at = @expiresAt, attempt_count = 0
                """;

            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(sql, new { verificationCode, expiresAt });
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Upsert failed: {ex.Message}");
        }
    }

    public async Task<Result<string?>> FindByEmailAsync(string email)
    {
        try
        {
            const string sql = """
                SELECT verification_code FROM pending_verifications
                WHERE email = @email AND expires_at > NOW()
                ORDER BY expires_at DESC
                LIMIT 1
                """;

            using var conn = _db.CreateConnection();
            var code = await conn.QueryFirstOrDefaultAsync<string?>(sql, new { email });
            return Result<string?>.Success(code);
        }
        catch (Exception ex)
        {
            return Result<string?>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IncrementAttemptsAsync(string verificationCode)
    {
        try
        {
            const string sql = """
                UPDATE pending_verifications
                SET attempt_count = attempt_count + 1
                WHERE verification_code = @verificationCode
                """;

            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { verificationCode });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Verification code not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Increment failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(string verificationCode)
    {
        try
        {
            const string sql = "DELETE FROM pending_verifications WHERE verification_code = @verificationCode";
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { verificationCode });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Verification code not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> StorePendingSessionAsync(Guid userId, string sessionCode, string deviceFingerprint, DateTime expiresAt)
    {
        try
        {
            const string sql = """
                INSERT INTO two_factor_pending_sessions (id, user_id, session_code, device_fingerprint, expires_at, attempt_count, created_at, is_completed)
                VALUES (@id, @userId, @sessionCode, @deviceFingerprint, @expiresAt, 0, NOW(), false)
                ON CONFLICT (session_code) DO UPDATE
                SET user_id = @userId, device_fingerprint = @deviceFingerprint,
                    expires_at = @expiresAt, attempt_count = 0, is_completed = false
                """;

            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(sql, new
            {
                id = Guid.NewGuid(),
                userId,
                sessionCode,
                deviceFingerprint,
                expiresAt
            });
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Store pending session failed: {ex.Message}");
        }
    }

    public async Task<Result<TwoFactorPendingSession?>> GetPendingSessionAsync(string sessionCode)
    {
        try
        {
            const string sql = """
                SELECT id, user_id as UserId, session_code as SessionCode,
                       device_fingerprint as DeviceFingerprint, attempt_count as AttemptCount,
                       created_at as CreatedAt, expires_at as ExpiresAt, is_completed as IsCompleted
                FROM two_factor_pending_sessions
                WHERE session_code = @sessionCode AND expires_at > NOW()
                """;

            using var conn = _db.CreateConnection();
            var session = await conn.QueryFirstOrDefaultAsync<TwoFactorPendingSession>(sql, new { sessionCode });
            return Result<TwoFactorPendingSession?>.Success(session);
        }
        catch (Exception ex)
        {
            return Result<TwoFactorPendingSession?>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> CompletePendingSessionAsync(string sessionCode)
    {
        try
        {
            const string sql = """
                UPDATE two_factor_pending_sessions
                SET is_completed = true
                WHERE session_code = @sessionCode
                """;

            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { sessionCode });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Session not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Complete session failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ExpirePendingSessionAsync(string sessionCode)
    {
        try
        {
            const string sql = """
                UPDATE two_factor_pending_sessions
                SET expires_at = NOW()
                WHERE session_code = @sessionCode
                """;

            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { sessionCode });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Session not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Expire session failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IncrementAttemptAsync(string sessionCode)
    {
        try
        {
            const string sql = """
                UPDATE two_factor_pending_sessions
                SET attempt_count = attempt_count + 1
                WHERE session_code = @sessionCode
                """;

            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { sessionCode });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Session not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Increment attempt failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> StoreRegistrationCodeAsync(Guid registrationRequestId, string code, DateTime expiresAt)
    {
        try
        {
            const string sql = """
                INSERT INTO device_registration_codes (registration_request_id, code, expires_at, created_at)
                VALUES (@registrationRequestId, @code, @expiresAt, NOW())
                ON CONFLICT (registration_request_id)
                DO UPDATE SET code = @code, expires_at = @expiresAt
                """;

            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(sql, new { registrationRequestId, code, expiresAt });
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Store registration code failed: {ex.Message}");
        }
    }

    public async Task<Result<string?>> ValidateRegistrationCodeAsync(Guid registrationRequestId)
    {
        try
        {
            const string sql = """
                SELECT code FROM device_registration_codes
                WHERE registration_request_id = @registrationRequestId AND expires_at > NOW()
                """;

            using var conn = _db.CreateConnection();
            var code = await conn.QueryFirstOrDefaultAsync<string?>(sql, new { registrationRequestId });
            return Result<string?>.Success(code);
        }
        catch (Exception ex)
        {
            return Result<string?>.Failure($"Validate failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> CleanupExpiredAsync()
    {
        try
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM pending_verifications WHERE expires_at <= NOW()");
            await conn.ExecuteAsync("DELETE FROM two_factor_pending_sessions WHERE expires_at <= NOW()");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Cleanup failed: {ex.Message}");
        }
    }
}
