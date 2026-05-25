extern alias GeekRepo;

using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories;
using Npgsql;

namespace GeekBackend.IntegrationTests;

internal static class IntegrationTestDataHelper
{
    public static async Task<UserTestContext> CreateUserAsync(
        string email,
        string password,
        bool twoFactorEnabled = false)
    {
        var connectionString = IntegrationTestConfig.DatabaseUrl
            ?? throw new InvalidOperationException("TEST_DATABASE_URL is required.");

        await SqlMigrationTestHelper.ApplyAsync();

        var factory = new NpgsqlConnectionFactory(connectionString);
        var users = new UserRepository(factory);

        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM users WHERE email = @email", conn);
            delete.Parameters.AddWithValue("email", email);
            await delete.ExecuteNonQueryAsync();
        }

        var subject = $"sub-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO users (id, email, username, subject, is_active, password_hash, two_factor_enabled)
                VALUES (@id, @email, @username, @subject, TRUE, @hash, @twoFactor)
                """, conn);
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("username", email.Split('@')[0]);
            cmd.Parameters.AddWithValue("subject", subject);
            cmd.Parameters.AddWithValue("hash", passwordHash);
            cmd.Parameters.AddWithValue("twoFactor", twoFactorEnabled);
            await cmd.ExecuteNonQueryAsync();
        }

        var found = await users.FindByIdAsync(userId);
        if (!found.IsSuccess || found.Value is null)
            throw new InvalidOperationException($"Failed to load test user: {found.Error}");

        var user = found.Value;

        user.TwoFactorEnabled = twoFactorEnabled;
        return new UserTestContext(user, email, password);
    }

    public static async Task DeleteUserAsync(Guid userId)
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
        var users = new UserRepository(factory);
        await users.DeleteAsync(userId);
    }
}

internal sealed record UserTestContext(
    GeekApplication.Entities.User User,
    string Email,
    string Password);
