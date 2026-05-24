using Dapper;
using GeekApplication.Entities.OpenIddict;
using GeekApplication.Interfaces.OpenIddict;
using GeekApplication.Results;
using GeekRepository.Infrastructure;
using GeekRepository.OpenIddict;

namespace GeekRepository.Repositories.OpenIddict;

public sealed class DapperApplicationRepository : IOpenIddictApplicationRepository
{
    private const string ReturningClause = """
        RETURNING
            id AS Id,
            application_type AS ApplicationType,
            client_id AS ClientId,
            client_secret AS ClientSecret,
            client_type AS ClientType,
            consent_type AS ConsentType,
            display_name AS DisplayName,
            display_names AS DisplayNames,
            json_web_key_set AS JsonWebKeySet,
            permissions AS Permissions,
            post_logout_redirect_uris AS PostLogoutRedirectUris,
            properties AS Properties,
            redirect_uris AS RedirectUris,
            requirements AS Requirements,
            settings AS Settings
        """;

    private const string SelectColumns = """
        id AS Id,
        application_type AS ApplicationType,
        client_id AS ClientId,
        client_secret AS ClientSecret,
        client_type AS ClientType,
        consent_type AS ConsentType,
        display_name AS DisplayName,
        display_names AS DisplayNames,
        json_web_key_set AS JsonWebKeySet,
        permissions AS Permissions,
        post_logout_redirect_uris AS PostLogoutRedirectUris,
        properties AS Properties,
        redirect_uris AS RedirectUris,
        requirements AS Requirements,
        settings AS Settings
        """;

    private readonly IDbConnectionFactory _db;

    public DapperApplicationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM openiddict_applications");
            return Result<int>.Success(count);
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(ex.Message);
        }
    }

    public async Task<Result<GeekOpenIddictApplication>> CreateAsync(
        GeekOpenIddictApplication application,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                INSERT INTO openiddict_applications (
                    id, application_type, client_id, client_secret, client_type, consent_type,
                    display_name, display_names, json_web_key_set, permissions,
                    post_logout_redirect_uris, properties, redirect_uris, requirements, settings)
                VALUES (
                    @Id, @ApplicationType, @ClientId, @ClientSecret, @ClientType, @ConsentType,
                    @DisplayName, @DisplayNames, @JsonWebKeySet, @Permissions,
                    @PostLogoutRedirectUris, @Properties, @RedirectUris, @Requirements, @Settings)
                """ + ReturningClause;

            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictApplication>(sql, application);
            return Result<GeekOpenIddictApplication>.Success(row);
        }
        catch (Exception ex)
        {
            return Result<GeekOpenIddictApplication>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(
                "DELETE FROM openiddict_applications WHERE id = @id",
                new { id });
            return Result<bool>.Success(rows > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }

    public async Task<Result<GeekOpenIddictApplication?>> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictApplication>(
                $"SELECT {SelectColumns} FROM openiddict_applications WHERE id = @id",
                new { id });
            return Result<GeekOpenIddictApplication?>.Success(row);
        }
        catch (Exception ex)
        {
            return Result<GeekOpenIddictApplication?>.Failure(ex.Message);
        }
    }

    public async Task<Result<GeekOpenIddictApplication?>> FindByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<GeekOpenIddictApplication>(
                $"SELECT {SelectColumns} FROM openiddict_applications WHERE client_id = @clientId",
                new { clientId });
            return Result<GeekOpenIddictApplication?>.Success(row);
        }
        catch (Exception ex)
        {
            return Result<GeekOpenIddictApplication?>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> FindByRedirectUriAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictApplication>(
                $"SELECT {SelectColumns} FROM openiddict_applications WHERE redirect_uris IS NOT NULL")).ToList();
            var matches = rows.Where(r => OpenIddictJson.JsonArrayContains(r.RedirectUris, uri)).ToList();
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Success(matches);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> FindByPostLogoutRedirectUriAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictApplication>(
                $"SELECT {SelectColumns} FROM openiddict_applications WHERE post_logout_redirect_uris IS NOT NULL")).ToList();
            var matches = rows.Where(r => OpenIddictJson.JsonArrayContains(r.PostLogoutRedirectUris, uri)).ToList();
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Success(matches);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> ListAsync(
        int? count,
        int? offset,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT {SelectColumns} FROM openiddict_applications ORDER BY id";
            if (count is not null)
                sql += " LIMIT @count OFFSET @offset";

            using var conn = _db.CreateConnection();
            var rows = (await conn.QueryAsync<GeekOpenIddictApplication>(
                sql, new { count, offset = offset ?? 0 })).ToList();
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Success(rows);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GeekOpenIddictApplication>>.Failure(ex.Message);
        }
    }

    public async Task<Result<GeekOpenIddictApplication>> UpdateAsync(
        GeekOpenIddictApplication application,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                UPDATE openiddict_applications SET
                    application_type = @ApplicationType,
                    client_id = @ClientId,
                    client_secret = @ClientSecret,
                    client_type = @ClientType,
                    consent_type = @ConsentType,
                    display_name = @DisplayName,
                    display_names = @DisplayNames,
                    json_web_key_set = @JsonWebKeySet,
                    permissions = @Permissions,
                    post_logout_redirect_uris = @PostLogoutRedirectUris,
                    properties = @Properties,
                    redirect_uris = @RedirectUris,
                    requirements = @Requirements,
                    settings = @Settings
                WHERE id = @Id
                """ + ReturningClause;

            using var conn = _db.CreateConnection();
            var row = await conn.QuerySingleAsync<GeekOpenIddictApplication>(sql, application);
            return Result<GeekOpenIddictApplication>.Success(row);
        }
        catch (Exception ex)
        {
            return Result<GeekOpenIddictApplication>.Failure(ex.Message);
        }
    }
}
