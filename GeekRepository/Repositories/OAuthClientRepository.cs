using System.Data;
using Dapper;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class OAuthClientRepository : IOAuthClientRepository
{
	private readonly IDbConnectionFactory _db;

	public OAuthClientRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<OauthClientEntity>> CreateAsync(CreateOAuthClientRequest request)
	{
		try
		{
			const string sql = """
				INSERT INTO oauth_clients (id, client_id, client_secret, redirect_uris)
				VALUES (@id, @clientId, @clientSecret, @redirectUris)
				RETURNING id, client_id as ClientId, client_secret as ClientSecret,
						  redirect_uris as RedirectUris, grant_types as GrantTypes,
						  response_types as ResponseTypes, scope as Scope,
						  token_endpoint_auth_method as TokenEndpointAuthMethod, created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var clientGuid = Guid.NewGuid();
			var client = await conn.QueryFirstOrDefaultAsync<OauthClientEntity>(sql, new
			{
				id = clientGuid,
				clientId = request.ClientId,
				clientSecret = request.ClientSecret,
				redirectUris = string.Join(",", request.RedirectUris)
			});

			return client != null
				? Result<OauthClientEntity>.Success(client)
				: Result<OauthClientEntity>.Failure("Failed to create client");
		}
		catch (Exception ex)
		{
			return Result<OauthClientEntity>.Failure($"Create client failed: {ex.Message}");
		}
	}

	public async Task<Result<OauthClientEntity>> FindByIdAsync(Guid clientId)
	{
		try
		{
			const string sql = """
				SELECT id, client_id as ClientId, client_name as ClientName,
					   client_secret as ClientSecret, redirect_uris as RedirectUris,
					   allowed_scopes as AllowedScopes, created_at as CreatedAt
				FROM oauth_clients
				WHERE id = @clientId
				""";

			using var conn = _db.CreateConnection();
			var client = await conn.QueryFirstOrDefaultAsync<OauthClientEntity>(sql, new { clientId });

			return client != null
				? Result<OauthClientEntity>.Success(client)
				: Result<OauthClientEntity>.NotFound("Client not found");
		}
		catch (Exception ex)
		{
			return Result<OauthClientEntity>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<OauthClientEntity>> FindByClientIdAsync(string clientId)
	{
		try
		{
			const string sql = """
				SELECT id, client_id as ClientId, client_name as ClientName,
					   client_secret as ClientSecret, redirect_uris as RedirectUris,
					   allowed_scopes as AllowedScopes, created_at as CreatedAt
				FROM oauth_clients
				WHERE client_id = @clientId
				""";

			using var conn = _db.CreateConnection();
			var client = await conn.QueryFirstOrDefaultAsync<OauthClientEntity>(sql, new { clientId });

			return client != null
				? Result<OauthClientEntity>.Success(client)
				: Result<OauthClientEntity>.NotFound("Client not found");
		}
		catch (Exception ex)
		{
			return Result<OauthClientEntity>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<OauthClientEntity>>> GetAllAsync()
	{
		try
		{
			const string sql = """
				SELECT id, client_id as ClientId, client_name as ClientName,
					   client_secret as ClientSecret, redirect_uris as RedirectUris,
					   allowed_scopes as AllowedScopes, created_at as CreatedAt
				FROM oauth_clients
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var clients = (await conn.QueryAsync<OauthClientEntity>(sql)).ToList();

			return Result<List<OauthClientEntity>>.Success(clients);
		}
		catch (Exception ex)
		{
			return Result<List<OauthClientEntity>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<OauthClientEntity>> UpdateAsync(OauthClientEntity client)
	{
		try
		{
			const string sql = """
				UPDATE oauth_clients
				SET client_secret = @clientSecret, redirect_uris = @redirectUris,
					grant_types = @grantTypes, response_types = @responseTypes,
					scope = @scope, token_endpoint_auth_method = @tokenEndpointAuthMethod
				WHERE id = @id
				RETURNING id, client_id as ClientId, client_secret as ClientSecret,
						  redirect_uris as RedirectUris, grant_types as GrantTypes,
						  response_types as ResponseTypes, scope as Scope,
						  token_endpoint_auth_method as TokenEndpointAuthMethod, created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var updated = await conn.QueryFirstOrDefaultAsync<OauthClientEntity>(sql, new
			{
				client.Id,
				client.ClientSecret,
				client.RedirectUris,
				client.GrantTypes,
				client.ResponseTypes,
				client.Scope,
				client.TokenEndpointAuthMethod
			});

			return updated != null
				? Result<OauthClientEntity>.Success(updated)
				: Result<OauthClientEntity>.NotFound("Client not found");
		}
		catch (Exception ex)
		{
			return Result<OauthClientEntity>.Failure($"Update failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> DeleteAsync(Guid clientId)
	{
		try
		{
			const string sql = """
				DELETE FROM oauth_clients
				WHERE id = @clientId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { clientId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Client not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Delete failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> ValidateClientSecretAsync(string clientId, string clientSecret)
	{
		try
		{
			const string sql = """
				SELECT client_secret FROM oauth_clients WHERE client_id = @clientId
				""";

			using var conn = _db.CreateConnection();
			var storedSecret = await conn.QueryFirstOrDefaultAsync<string>(sql, new { clientId });

			if (storedSecret == null)
				return Result<bool>.NotFound("Client not found");

			var isValid = BCrypt.Net.BCrypt.Verify(clientSecret, storedSecret);
			return Result<bool>.Success(isValid);
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Validation failed: {ex.Message}");
		}
	}
}
