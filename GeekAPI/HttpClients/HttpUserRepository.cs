using System.Net;
using System.Net.Http.Json;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpUserRepository : IUserRepository
{
    private readonly HttpClient _http;

    public HttpUserRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<User>> CreateAsync(CreateUserRequest request)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/users", request);
        return await ReadResult<User>(response);
    }

    public async Task<Result<User>> FindByIdAsync(Guid userId)
    {
        var response = await _http.GetAsync($"repo/auth/users/{userId}");
        return await ReadResult<User>(response);
    }

    public async Task<Result<User>> FindByEmailAsync(string email)
    {
        var response = await _http.GetAsync($"repo/auth/users/by-email/{Uri.EscapeDataString(email)}");
        return await ReadResult<User>(response);
    }

    public async Task<Result<User>> FindByUsernameAsync(string username)
    {
        var response = await _http.GetAsync($"repo/auth/users/by-username/{Uri.EscapeDataString(username)}");
        return await ReadResult<User>(response);
    }

    public async Task<Result<User>> FindBySubjectAsync(string subject)
    {
        var response = await _http.GetAsync($"repo/auth/users/by-subject/{Uri.EscapeDataString(subject)}");
        return await ReadResult<User>(response);
    }

    public async Task<Result<User>> UpdateAsync(User user)
    {
        var response = await _http.PatchAsJsonAsync($"repo/auth/users/{user.Id}", user);
        return await ReadResult<User>(response);
    }

    public async Task<Result<bool>> DeleteAsync(Guid userId)
    {
        var response = await _http.DeleteAsync($"repo/auth/users/{userId}");
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> VerifyPasswordAsync(Guid userId, string password)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/users/{userId}/verify-password", new { password });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> UpdatePasswordAsync(Guid userId, string newPassword)
    {
        var response = await _http.PostAsJsonAsync($"repo/auth/users/{userId}/password", new { password = newPassword });
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
