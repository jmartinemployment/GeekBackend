using Dapper;
using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories.OpenIddict;

public sealed class DapperTokenRepository : IOpenIddictTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public DapperTokenRepository(IDbConnectionFactory db) => _db = db;

    private const string SelectColumns = """
        id as Id, application_id as ApplicationId, authorization_id as AuthorizationId,
        concurrency_token as ConcurrencyToken, creation_date as CreationDate,
        expiration_date as ExpirationDate, payload as Payload, properties as Properties,
        redemption_date as RedemptionDate, reference_id as ReferenceId, status as Status,
        subject as Subject, type as Type
        """;

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM openiddict_tokens");
            return Result<int>.Success(count);
        }
        catch (Exception ex) { return Result<int>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictToken>> CreateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"""
                INSERT INTO openiddict_tokens (id, application_id, authorization_id, concurrency_token, creation_date,
                    expiration_date, payload, properties, redemption_date, reference_id, status, subject, type)
                VALUES (@Id, @ApplicationId, @AuthorizationId, @ConcurrencyToken, @CreationDate,
                    @ExpirationDate, @Payload, @Properties, @RedemptionDate, @ReferenceId, @Status, @Subject, @Type)
                RETURNING {SelectColumns}
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictToken>(sql, token);
            return Result<GeekOpenIddictToken>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictToken>.Failure(ex.Message); }
    }

    public async Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync("DELETE FROM openiddict_tokens WHERE id = @id", new { id });
            return Result<bool>.Success(rows > 0);
        }
        catch (Exception ex) { return Result<bool>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictToken?>> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_tokens WHERE id = @id";
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictToken>(sql, new { id });
            return Result<GeekOpenIddictToken?>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictToken?>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictToken?>> FindByReferenceIdAsync(string referenceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_tokens WHERE reference_id = @referenceId";
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictToken>(sql, new { referenceId });
            return Result<GeekOpenIddictToken?>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictToken?>.Failure(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictToken>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_tokens ORDER BY id";
            if (count is not null) sql += " LIMIT @count OFFSET @offset";
            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictToken>(sql, new { count, offset = offset ?? 0 })).ToList();
            return Result<IReadOnlyList<GeekOpenIddictToken>>.Success(rows);
        }
        catch (Exception ex) { return Result<IReadOnlyList<GeekOpenIddictToken>>.Failure(ex.Message); }
    }

    public async Task<Result<GeekOpenIddictToken>> UpdateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"""
                UPDATE openiddict_tokens SET application_id = @ApplicationId, authorization_id = @AuthorizationId,
                    concurrency_token = @ConcurrencyToken, creation_date = @CreationDate, expiration_date = @ExpirationDate,
                    payload = @Payload, properties = @Properties, redemption_date = @RedemptionDate,
                    reference_id = @ReferenceId, status = @Status, subject = @Subject, type = @Type
                WHERE id = @Id
                RETURNING {SelectColumns}
                """;
            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictToken>(sql, token);
            return Result<GeekOpenIddictToken>.Success(row);
        }
        catch (Exception ex) { return Result<GeekOpenIddictToken>.Failure(ex.Message); }
    }

    public async Task<Result<int>> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(
                "UPDATE openiddict_tokens SET status = 'revoked' WHERE subject = @subject AND status <> 'revoked'",
                new { subject });
            return Result<int>.Success(rows);
        }
        catch (Exception ex) { return Result<int>.Failure(ex.Message); }
    }
}
