using System.Net;
using System.Net.Http.Json;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpDeviceRepository : IDeviceOauthRepository
{
    private readonly HttpClient _http;

    public HttpDeviceRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<DeviceOauth>> UpsertAsync(Guid userId, UpsertDeviceRequest request)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/devices/{userId}", request);
        return await ReadResult<DeviceOauth>(response);
    }

    public async Task<Result<DeviceOauth>> FindByIdAsync(Guid deviceId)
    {
        var response = await _http.GetAsync($"repo/auth/devices/{deviceId}");
        return await ReadResult<DeviceOauth>(response);
    }

    public async Task<Result<List<DeviceOauth>>> FindByUserIdAsync(Guid userId)
    {
        var response = await _http.GetAsync($"repo/auth/devices/by-user/{userId}");
        return await ReadResult<List<DeviceOauth>>(response);
    }

    public async Task<Result<DeviceOauth>> UpdateAsync(DeviceOauth device)
    {
        var response = await _http.PatchAsJsonAsync($"repo/auth/devices/{device.Id}", device);
        return await ReadResult<DeviceOauth>(response);
    }

    public async Task<Result<bool>> DeleteAsync(Guid deviceId)
    {
        var response = await _http.DeleteAsync($"repo/auth/devices/{deviceId}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<DeviceOauth>> RegisterAsync(Guid userId, RegisterDeviceOauthRequest request)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/devices/{userId}/register", request);
        return await ReadResult<DeviceOauth>(response);
    }

    public async Task<Result<DeviceOauth>> FindByFingerprintAsync(Guid userId, string fingerprint)
    {
        var response = await _http.GetAsync($"repo/auth/devices/by-fingerprint/{userId}/{Uri.EscapeDataString(fingerprint)}");
        return await ReadResult<DeviceOauth>(response);
    }

    public async Task<Result<List<DeviceOauth>>> GetUserDevicesAsync(Guid userId)
    {
        var response = await _http.GetAsync($"repo/auth/devices/by-user/{userId}");
        return await ReadResult<List<DeviceOauth>>(response);
    }

    public async Task<Result<List<DeviceOauth>>> GetActiveDevicesAsync(Guid userId)
    {
        var response = await _http.GetAsync($"repo/auth/devices/by-user/{userId}/active");
        return await ReadResult<List<DeviceOauth>>(response);
    }

    public async Task<Result<bool>> RevokeAsync(Guid deviceId)
    {
        var response = await _http.PostAsync($"repo/auth/devices/{deviceId}/revoke", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> TrustAsync(Guid deviceId, int trustDaysOrNull = 30)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/devices/{deviceId}/trust", new { days = trustDaysOrNull });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<string>> IssueChallengeAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"repo/auth/devices/{deviceId}/challenge", null, cancellationToken);
        return await ReadResult<string>(response);
    }

    public async Task<Result<bool>> VerifyChallengeAsync(
        Guid deviceId,
        string nonce,
        string signatureBase64,
        string? publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/auth/devices/{deviceId}/verify",
            new { nonce, signature = signatureBase64, publicKeyPem },
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
