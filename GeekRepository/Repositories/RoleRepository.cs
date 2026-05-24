using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _db;

    public RoleRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<Role>> CreateAsync(string name, string? description)
    {
        try
        {
            const string sql = """
				INSERT INTO roles (id, name, description)
				VALUES (@id, @name, @description)
				RETURNING id, name, description, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var roleId = Guid.NewGuid();
            var role = await conn.QueryFirstOrDefaultAsync<Role>(sql, new
            {
                id = roleId,
                name,
                description
            });

            return role != null
                ? Result<Role>.Success(role)
                : Result<Role>.Failure("Failed to create role");
        }
        catch (Exception ex)
        {
            return Result<Role>.Failure($"Create role failed: {ex.Message}");
        }
    }

    public async Task<Result<Role>> FindByIdAsync(Guid roleId)
    {
        try
        {
            const string sql = """
				SELECT id, name, description, created_at as CreatedAt
				FROM roles
				WHERE id = @roleId
				""";

            using var conn = _db.CreateConnection();
            var role = await conn.QueryFirstOrDefaultAsync<Role>(sql, new { roleId });

            return role != null
                ? Result<Role>.Success(role)
                : Result<Role>.NotFound("Role not found");
        }
        catch (Exception ex)
        {
            return Result<Role>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<Role>> FindByNameAsync(string name)
    {
        try
        {
            const string sql = """
				SELECT id, name, description, created_at as CreatedAt
				FROM roles
				WHERE name = @name
				""";

            using var conn = _db.CreateConnection();
            var role = await conn.QueryFirstOrDefaultAsync<Role>(sql, new { name });

            return role != null
                ? Result<Role>.Success(role)
                : Result<Role>.NotFound("Role not found");
        }
        catch (Exception ex)
        {
            return Result<Role>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<Role>>> GetAllAsync()
    {
        try
        {
            const string sql = """
				SELECT id, name, description, created_at as CreatedAt
				FROM roles
				ORDER BY name
				""";

            using var conn = _db.CreateConnection();
            var roles = (await conn.QueryAsync<Role>(sql)).ToList();

            return Result<List<Role>>.Success(roles);
        }
        catch (Exception ex)
        {
            return Result<List<Role>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<Role>> UpdateAsync(Guid roleId, string name, string? description)
    {
        try
        {
            const string sql = """
				UPDATE roles
				SET name = @name, description = @description
				WHERE id = @roleId
				RETURNING id, name, description, created_at as CreatedAt
				""";

            using var conn = _db.CreateConnection();
            var role = await conn.QueryFirstOrDefaultAsync<Role>(sql, new { roleId, name, description });

            return role != null
                ? Result<Role>.Success(role)
                : Result<Role>.NotFound("Role not found");
        }
        catch (Exception ex)
        {
            return Result<Role>.Failure($"Update failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(Guid roleId)
    {
        try
        {
            const string sql = """
				DELETE FROM roles
				WHERE id = @roleId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { roleId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Role not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }
}
