using Dapper;
using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.OpenIddict;

public sealed class DapperScopeRepository : IOpenIddictScopeRepository
{
    private readonly IDbConnectionFactory _db;

    public DapperScopeRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM openiddict_scopes");
            return Result<int>.Success(count);
        }
        catch (Exception ex) { return Result<int>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictScope>> CreateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                INSERT INTO openiddict_scopes (id, description, descriptions, display_name, display_names, name, properties, resources)
                VALUES (@Id, @Description, @Descriptions, @DisplayName, @DisplayNames, @Name, @Properties, @Resources)
                RETURNING id as Id, description as Description, descriptions as Descriptions,
                          display_name as DisplayName, display_names as DisplayNames, name as Name,
                          properties as Properties, resources as Resources
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictScope>(sql, scope);
            return Result<GeekOpenIddictScope>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictScope>.Failure(ex.Message); }
    }

    public async Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync("DELETE FROM openiddict_scopes WHERE id = @id", new { id });
            return Result<bool>.Success(rows > 0);
        }
        catch (Exception ex) { return Result<bool>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictScope?>> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictScope>(
                "SELECT id as Id, description as Description, descriptions as Descriptions, display_name as DisplayName, display_names as DisplayNames, name as Name, properties as Properties, resources as Resources FROM openiddict_scopes WHERE id = @id",
                new { id });
            return Result<GeekOpenIddictScope?>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictScope?>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictScope?>> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictScope>(
                "SELECT id as Id, description as Description, descriptions as Descriptions, display_name as DisplayName, display_names as DisplayNames, name as Name, properties as Properties, resources as Resources FROM openiddict_scopes WHERE name = @name",
                new { name });
            return Result<GeekOpenIddictScope?>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictScope?>.Failure(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictScope>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = """
                SELECT id as Id, description as Description, descriptions as Descriptions,
                       display_name as DisplayName, display_names as DisplayNames, name as Name,
                       properties as Properties, resources as Resources
                FROM openiddict_scopes ORDER BY id
                """;
            if (count is not null) sql += " LIMIT @count OFFSET @offset";
            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictScope>(sql, new { count, offset = offset ?? 0 })).ToList();
            return Result<IReadOnlyList<GeekOpenIddictScope>>.Success(rows);
        }
        catch (Exception ex) { return Result<IReadOnlyList<GeekOpenIddictScope>>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictScope>> UpdateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                UPDATE openiddict_scopes SET description = @Description, descriptions = @Descriptions,
                    display_name = @DisplayName, display_names = @DisplayNames, name = @Name,
                    properties = @Properties, resources = @Resources
                WHERE id = @Id
                RETURNING id as Id, description as Description, descriptions as Descriptions,
                          display_name as DisplayName, display_names as DisplayNames, name as Name,
                          properties as Properties, resources as Resources
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictScope>(sql, scope);
            return Result<GeekOpenIddictScope>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictScope>.Failure(ex.Message); }
    }
}
