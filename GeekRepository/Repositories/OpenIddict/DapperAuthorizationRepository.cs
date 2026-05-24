using Dapper;
using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.OpenIddict;

public sealed class DapperAuthorizationRepository : IOpenIddictAuthorizationRepository
{
    private readonly IDbConnectionFactory _db;

    public DapperAuthorizationRepository(IDbConnectionFactory db) => _db = db;

    private const string SelectColumns = """
        id as Id, application_id as ApplicationId, concurrency_token as ConcurrencyToken,
        creation_date as CreationDate, properties as Properties, scopes as Scopes,
        status as Status, subject as Subject, type as Type
        """;

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM openiddict_authorizations");
            return Result<int>.Success(count);
        }
        catch (Exception ex) { return Result<int>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictAuthorization>> CreateAsync(
        GeekOpenIddictAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"""
                INSERT INTO openiddict_authorizations (id, application_id, concurrency_token, creation_date, properties, scopes, status, subject, type)
                VALUES (@Id, @ApplicationId, @ConcurrencyToken, @CreationDate, @Properties, @Scopes, @Status, @Subject, @Type)
                RETURNING {SelectColumns}
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictAuthorization>(sql, authorization);
            return Result<GeekOpenIddictAuthorization>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictAuthorization>.Failure(ex.Message); }
    }

    public async Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync("DELETE FROM openiddict_authorizations WHERE id = @id", new { id });
            return Result<bool>.Success(rows > 0);
        }
        catch (Exception ex) { return Result<bool>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictAuthorization?>> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_authorizations WHERE id = @id";
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictAuthorization>(sql, new { id });
            return Result<GeekOpenIddictAuthorization?>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictAuthorization?>.Failure(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictAuthorization>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_authorizations ORDER BY id";
            if (count is not null) sql += " LIMIT @count OFFSET @offset";
            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictAuthorization>(sql, new { count, offset = offset ?? 0 })).ToList();
            return Result<IReadOnlyList<GeekOpenIddictAuthorization>>.Success(rows);
        }
        catch (Exception ex) { return Result<IReadOnlyList<GeekOpenIddictAuthorization>>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictAuthorization>> UpdateAsync(
        GeekOpenIddictAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"""
                UPDATE openiddict_authorizations SET application_id = @ApplicationId, concurrency_token = @ConcurrencyToken,
                    creation_date = @CreationDate, properties = @Properties, scopes = @Scopes, status = @Status,
                    subject = @Subject, type = @Type
                WHERE id = @Id
                RETURNING {SelectColumns}
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictAuthorization>(sql, authorization);
            return Result<GeekOpenIddictAuthorization>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictAuthorization>.Failure(ex.Message); }
    }
}
