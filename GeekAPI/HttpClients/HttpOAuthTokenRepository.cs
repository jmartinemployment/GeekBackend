using System.Net;
using System.Net.Http.Json;
using GeekApplication.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpOAuthTokenRepository : IOAuthTokenRepository
{
    private readonly HttpClient _http;

    public HttpOAuthTokenRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<CreateOAuthTokenRequest>> CreateAsync(CreateOAuthTokenRequest request)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/tokens", request);
        return await ReadResult<CreateOAuthTokenRequest>(response);
    }

    public async Task<Result<CreateOAuthTokenRequest>> FindByAccessTokenAsync(string accessToken)
    {
        var response = await _http.GetAsync($"repo/auth/tokens/by-access/{Uri.EscapeDataString(accessToken)}");
        return await ReadResult<CreateOAuthTokenRequest>(response);
    }

    public async Task<Result<CreateOAuthTokenRequest>> FindByRefreshTokenAsync(string refreshToken)
    {
        var response = await _http.GetAsync($"repo/auth/tokens/by-refresh/{Uri.EscapeDataString(refreshToken)}");
        return await ReadResult<CreateOAuthTokenRequest>(response);
    }

    public async Task<Result<CreateOAuthTokenRequest>> UpdateAsync(string tokenId, UpdateOAuthTokenRequest request)
    {
        var response = await _http.PatchAsJsonAsync($"repo/auth/tokens/{tokenId}", request);
        return await ReadResult<CreateOAuthTokenRequest>(response);
    }

    public async Task<Result<bool>> DeleteAsync(string tokenId)
    {
        var response = await _http.DeleteAsync($"repo/auth/tokens/{tokenId}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> RevokeTokenAsync(string jti, string reason)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/tokens/{jti}/revoke", new { reason });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> IsTokenBlacklistedAsync(string jti)
    {
        var response = await _http.GetAsync($"repo/auth/tokens/{jti}/blacklisted");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> AddToBlacklistAsync(string jti, Guid userId, DateTime expiresAt, string reason)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/tokens/blacklist", new { jti, userId, expiresAt, reason });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<List<string>>> GetUserBlacklistedJtisAsync(Guid userId)
    {
        var response = await _http.GetAsync($"repo/auth/tokens/blacklist/{userId}");
        return await ReadResult<List<string>>(response);
    }

    public async Task<Result<bool>> CleanupExpiredBlacklistEntriesAsync()
    {
        var response = await _http.PostAsync("repo/auth/tokens/blacklist/cleanup", null);
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
}
