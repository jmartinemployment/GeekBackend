using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpOidcStorageRepository : IOidcStorageRepository
{
    private readonly HttpClient _http;

    public HttpOidcStorageRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<bool>> UpsertAsync(OidcStorage storage)
    {
        var payload = new
        {
            storage.Id,
            storage.Kind,
            Payload = JsonSerializer.Deserialize<Dictionary<string, object>>(storage.Payload) ?? new(),
            ExpiresIn = (int)(storage.ExpiresAt - DateTime.UtcNow).TotalMinutes,
            storage.UserCode,
            storage.TokenHash,
            storage.Uid,
            storage.GrantId
        };
        var response = await _http.PostAsJsonAsync("repo/auth/oidc-storage", payload);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<OidcStorage?>> FindAsync(string kind, string key)
    {
        var response = await _http.GetAsync($"repo/auth/oidc-storage/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(key)}");
        return await ReadNullableResult<OidcStorage>(response);
    }

    public async Task<Result<OidcStorage?>> FindByUidAsync(string uid)
    {
        var response = await _http.GetAsync($"repo/auth/oidc-storage/by-uid/{Uri.EscapeDataString(uid)}");
        return await ReadNullableResult<OidcStorage>(response);
    }

    public async Task<Result<OidcStorage?>> FindByUserCodeAsync(string userCode)
    {
        var response = await _http.GetAsync($"repo/auth/oidc-storage/by-usercode/{Uri.EscapeDataString(userCode)}");
        return await ReadNullableResult<OidcStorage>(response);
    }

    public async Task<Result<bool>> DestroyAsync(string key)
    {
        var response = await _http.DeleteAsync($"repo/auth/oidc-storage/{Uri.EscapeDataString(key)}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> ConsumeAsync(string key)
    {
        var response = await _http.PostAsync($"repo/auth/oidc-storage/{Uri.EscapeDataString(key)}/consume", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> RevokeByGrantIdAsync(string grantId)
    {
        var response = await _http.PostAsync($"repo/auth/oidc-storage/revoke-by-grant/{Uri.EscapeDataString(grantId)}", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> StoreNonceAsync(string nonce, string clientId, DateTime expiresAt)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/oidc-storage/nonces", new { nonce, clientId, expiresAt });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> ValidateNonceAsync(string nonce, string clientId)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/oidc-storage/nonces/validate", new { nonce, clientId });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> RevokeNonceAsync(string nonce)
    {
        var response = await _http.PostAsync($"repo/auth/oidc-storage/nonces/{Uri.EscapeDataString(nonce)}/revoke", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> StoreAuthorizationCodeAsync(string code, Guid userId, string clientId, List<string> scopes, DateTime expiresAt)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/oidc-storage/auth-codes", new { code, userId, clientId, scopes, expiresAt });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<(Guid UserId, string ClientId, List<string> Scopes)?>> ValidateAuthorizationCodeAsync(string code)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/oidc-storage/auth-codes/validate", new { code });
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<(Guid, string, List<string>)?>.Success(null);
        if (!response.IsSuccessStatusCode)
            return Result<(Guid, string, List<string>)?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<AuthCodeValidationResult>>();
        if (wrapper?.Data == null) return Result<(Guid, string, List<string>)?>.Success(null);
        return Result<(Guid, string, List<string>)?>.Success((wrapper.Data.UserId, wrapper.Data.ClientId, wrapper.Data.Scopes));
    }

    public async Task<Result<bool>> RevokeAuthorizationCodeAsync(string code)
    {
        var response = await _http.PostAsync($"repo/auth/oidc-storage/auth-codes/{Uri.EscapeDataString(code)}/revoke", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> CleanupExpiredAsync()
    {
        var response = await _http.PostAsync("repo/auth/oidc-storage/cleanup", null);
        return await ReadResult<bool>(response);
    }

    private static async Task<Result<T>> ReadResult<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<T>.NotFound("Not found");
        if (!response.IsSuccessStatusCode)
            return Result<T>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        if (wrapper is null)
            return Result<T>.Failure("Invalid response from repository");
        return Result<T>.Success(wrapper.Data);
    }

    private static async Task<Result<T?>> ReadNullableResult<T>(HttpResponseMessage response) where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<T?>.Success(null);
        if (!response.IsSuccessStatusCode)
            return Result<T?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        T? data = wrapper?.Data;
        return Result<T?>.Success(data);
    }

    private sealed record AuthCodeValidationResult(Guid UserId, string ClientId, List<string> Scopes);
}
