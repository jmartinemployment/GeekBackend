using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpPendingVerificationRepository : IPendingVerificationRepository
{
    private readonly HttpClient _http;

    public HttpPendingVerificationRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<bool>> UpsertAsync(string verificationCode, DateTime expiresAt)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/pending-verifications",
            new { userId = Guid.Empty, verificationType = "email", verificationCode, expiresAt });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<string?>> FindByEmailAsync(string email)
    {
        var response = await _http.GetAsync($"repo/auth/pending-verifications/by-email/{Uri.EscapeDataString(email)}");
        if (response.StatusCode == HttpStatusCode.NotFound) return Result<string?>.Success(null);
        if (!response.IsSuccessStatusCode) return Result<string?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        return Result<string?>.Success(wrapper?.Data);
    }

    public async Task<Result<bool>> IncrementAttemptsAsync(string verificationCode)
    {
        var response = await _http.PatchAsync($"repo/auth/pending-verifications/{Uri.EscapeDataString(verificationCode)}/increment-attempts", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> DeleteAsync(string verificationCode)
    {
        var response = await _http.DeleteAsync($"repo/auth/pending-verifications/{Uri.EscapeDataString(verificationCode)}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> StorePendingSessionAsync(Guid userId, string sessionCode, string deviceFingerprint, DateTime expiresAt)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/pending-verifications/sessions", new { userId, sessionCode, deviceFingerprint, expiresAt });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<TwoFactorPendingSession?>> GetPendingSessionAsync(string sessionCode)
    {
        var response = await _http.GetAsync($"repo/auth/pending-verifications/sessions/{Uri.EscapeDataString(sessionCode)}");
        if (response.StatusCode == HttpStatusCode.NotFound) return Result<TwoFactorPendingSession?>.Success(null);
        if (!response.IsSuccessStatusCode) return Result<TwoFactorPendingSession?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<TwoFactorPendingSession>>();
        return Result<TwoFactorPendingSession?>.Success(wrapper?.Data);
    }

    public async Task<Result<bool>> CompletePendingSessionAsync(string sessionCode)
    {
        var response = await _http.PostAsync($"repo/auth/pending-verifications/sessions/{Uri.EscapeDataString(sessionCode)}/complete", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> ExpirePendingSessionAsync(string sessionCode)
    {
        var response = await _http.PostAsync($"repo/auth/pending-verifications/sessions/{Uri.EscapeDataString(sessionCode)}/expire", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> IncrementAttemptAsync(string sessionCode)
    {
        var response = await _http.PatchAsync($"repo/auth/pending-verifications/sessions/{Uri.EscapeDataString(sessionCode)}/increment-attempt", null);
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> StoreRegistrationCodeAsync(Guid registrationRequestId, string code, DateTime expiresAt)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/pending-verifications/registration-codes", new { registrationRequestId, code, expiresAt });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<string?>> ValidateRegistrationCodeAsync(Guid registrationRequestId)
    {
        var response = await _http.GetAsync($"repo/auth/pending-verifications/registration-codes/{registrationRequestId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return Result<string?>.Success(null);
        if (!response.IsSuccessStatusCode) return Result<string?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        return Result<string?>.Success(wrapper?.Data);
    }

    public async Task<Result<bool>> CleanupExpiredAsync()
    {
        var response = await _http.PostAsync("repo/auth/pending-verifications/cleanup", null);
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
