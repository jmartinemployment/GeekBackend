using Dapper;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public class OAuthClientRepository : IOAuthClientRepository
{
    private readonly AppDbContext _context;

    public OAuthClientRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OauthClient>> FindByClientIdAsync(string clientId)
    {
        try
        {
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            var sql = """
                SELECT id, client_id, client_secret, redirect_uris, grant_types, response_types, scope,
                       token_endpoint_auth_method, created_at, updated_at
                FROM geek_auth.oauth_client
                WHERE client_id = @ClientId
                """;

            var result = await connection.QueryFirstOrDefaultAsync<OauthClient>(
                sql,
                new { ClientId = clientId }
            );

            return result != null
                ? Result<OauthClient>.Success(result)
                : Result<OauthClient>.NotFound("OAuthClient not found");
        }
        catch (Exception ex)
        {
            return Result<OauthClient>.Failure(ex.Message);
        }
    }

    public async Task<Result<OauthClient>> CreateAsync(CreateOAuthClientRequest request)
    {
        try
        {
            var client = new OauthClient
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = request.ClientId,
                ClientSecret = request.ClientSecret,
                RedirectUris = System.Text.Json.JsonSerializer.Serialize(request.RedirectUris),
                GrantTypes = System.Text.Json.JsonSerializer.Serialize(request.GrantTypes),
                ResponseTypes = System.Text.Json.JsonSerializer.Serialize(request.ResponseTypes),
                Scope = request.Scope,
                TokenEndpointAuthMethod = request.TokenEndpointAuthMethod,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.OAuthClients.Add(client);
            await _context.SaveChangesAsync();

            return Result<OauthClient>.Success(client);
        }
        catch (Exception ex)
        {
            return Result<OauthClient>.Failure(ex.Message);
        }
    }
}
