using System.Net;
using System.Net.Http.Json;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpUserSecretsRepository : IUserSecretsRepository
{
    private readonly HttpClient _http;

    public HttpUserSecretsRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<string?>> GetTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"repo/auth/user-secrets/{userId}/totp", cancellationToken);
        return await ReadResult<string?>(response);
    }

    public async Task<Result<bool>> SetTotpSecretAsync(Guid userId, string base32Secret, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"repo/auth/user-secrets/{userId}/totp",
            new { secret = base32Secret },
            cancellationToken);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> ClearTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"repo/auth/user-secrets/{userId}/totp", cancellationToken);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<IReadOnlyList<string>>> GetRecoveryCodeHashesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"repo/auth/user-secrets/{userId}/recovery-codes", cancellationToken);
        return await ReadResult<IReadOnlyList<string>>(response);
    }

    public async Task<Result<bool>> SetRecoveryCodeHashesAsync(Guid userId, IReadOnlyList<string> hashedCodes, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"repo/auth/user-secrets/{userId}/recovery-codes",
            new { hashes = hashedCodes },
            cancellationToken);
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
