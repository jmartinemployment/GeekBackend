using System.Data;
using Dapper;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<User>> CreateAsync(CreateUserRequest request)
    {
        try
        {
            const string sql = """
				INSERT INTO users (id, email, username, subject, is_active)
				VALUES (@id, @email, @username, @subject, @isActive)
				RETURNING id, email, username, subject, is_active as IsActive, created_at as CreatedAt, updated_at as UpdatedAt
				""";

            using var conn = _db.CreateConnection();
            var userId = Guid.NewGuid();
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new
            {
                id = userId,
                email = request.Email,
                username = request.Username,
                subject = request.Subject,
                isActive = true
            });

            return user != null
                ? Result<User>.Success(user)
                : Result<User>.Failure("Failed to create user");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Create user failed: {ex.Message}");
        }
    }

    public async Task<Result<User>> FindByIdAsync(Guid userId)
    {
        try
        {
            const string sql = """
				SELECT id, email, username, subject, is_active as IsActive,
					   created_at as CreatedAt, updated_at as UpdatedAt
				FROM users
				WHERE id = @userId
				""";

            using var conn = _db.CreateConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { userId });

            return user != null
                ? Result<User>.Success(user)
                : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<User>> FindByUsernameAsync(string username)
    {
        try
        {
            const string sql = """
				SELECT id, email, username, subject, is_active as IsActive,
					   created_at as CreatedAt, updated_at as UpdatedAt
				FROM users
				WHERE username = @username
				""";

            using var conn = _db.CreateConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { username });

            return user != null
                ? Result<User>.Success(user)
                : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<User>> FindByEmailAsync(string email)
    {
        try
        {
            const string sql = """
				SELECT id, email, username, subject, is_active as IsActive,
					   created_at as CreatedAt, updated_at as UpdatedAt
				FROM users
				WHERE email = @email
				""";

            using var conn = _db.CreateConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { email });

            return user != null
                ? Result<User>.Success(user)
                : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<User>> FindBySubjectAsync(string subject)
    {
        try
        {
            const string sql = """
				SELECT id, email, username, subject, is_active as IsActive,
					   created_at as CreatedAt, updated_at as UpdatedAt
				FROM users
				WHERE subject = @subject
				""";

            using var conn = _db.CreateConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { subject });

            return user != null
                ? Result<User>.Success(user)
                : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<User>> UpdateAsync(User user)
    {
        try
        {
            const string sql = """
				UPDATE users
				SET email = @email, username = @username, subject = @subject,
					is_active = @isActive, updated_at = CURRENT_TIMESTAMP
				WHERE id = @id
				RETURNING id, email, username, subject, is_active as IsActive,
						  created_at as CreatedAt, updated_at as UpdatedAt
				""";

            using var conn = _db.CreateConnection();
            var updated = await conn.QueryFirstOrDefaultAsync<User>(sql, new
            {
                user.Id,
                user.Email,
                user.Username,
                user.Subject
            });

            return updated != null
                ? Result<User>.Success(updated)
                : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure($"Update failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(Guid userId)
    {
        try
        {
            const string sql = """
				DELETE FROM users
				WHERE id = @userId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { userId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyPasswordAsync(Guid userId, string password)
    {
        try
        {
            const string sql = """
				SELECT password_hash FROM users WHERE id = @userId
				""";

            using var conn = _db.CreateConnection();
            var hash = await conn.QueryFirstOrDefaultAsync<string>(sql, new { userId });

            if (hash == null)
                return Result<bool>.NotFound("User not found");

            var isValid = BCrypt.Net.BCrypt.Verify(password, hash);
            return Result<bool>.Success(isValid);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Verification failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> UpdatePasswordAsync(Guid userId, string newPassword)
    {
        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);

            const string sql = """
				UPDATE users
				SET password_hash = @hash, updated_at = CURRENT_TIMESTAMP
				WHERE id = @userId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { hash, userId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Password update failed: {ex.Message}");
        }
    }
}
