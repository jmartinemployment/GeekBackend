using System.Net;
using System.Net.Http.Json;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpOAuthClientRepository : IOAuthClientRepository
{
    private readonly HttpClient _http;

    public HttpOAuthClientRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<OauthClientEntity>> CreateAsync(CreateOAuthClientRequest request)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/clients", request);
        return await ReadResult<OauthClientEntity>(response);
    }

    public async Task<Result<OauthClientEntity>> FindByIdAsync(Guid clientId)
    {
        var response = await _http.GetAsync($"repo/auth/clients/{clientId}");
        return await ReadResult<OauthClientEntity>(response);
    }

    public async Task<Result<OauthClientEntity>> FindByClientIdAsync(string clientId)
    {
        var response = await _http.GetAsync($"repo/auth/clients/by-id/{Uri.EscapeDataString(clientId)}");
        return await ReadResult<OauthClientEntity>(response);
    }

    public async Task<Result<List<OauthClientEntity>>> GetAllAsync()
    {
        var response = await _http.GetAsync("repo/auth/clients");
        return await ReadResult<List<OauthClientEntity>>(response);
    }

    public async Task<Result<OauthClientEntity>> UpdateAsync(OauthClientEntity client)
    {
        var response = await _http.PutAsJsonAsync($"repo/auth/clients/{client.Id}", client);
        return await ReadResult<OauthClientEntity>(response);
    }

    public async Task<Result<bool>> DeleteAsync(Guid clientId)
    {
        var response = await _http.DeleteAsync($"repo/auth/clients/{clientId}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> ValidateClientSecretAsync(string clientId, string clientSecret)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/clients/validate-secret", new { clientId, clientSecret });
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
