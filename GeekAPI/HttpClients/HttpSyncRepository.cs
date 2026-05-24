using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpSyncRepository : ISyncRepository
{
    private readonly HttpClient _http;

    public HttpSyncRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public Task<Result<SyncQueue>> EnqueueAsync(Guid userId, Guid targetDeviceId, string payload) =>
        PostAsync<SyncQueue>("repo/sync/enqueue", new { userId, targetDeviceId, payload });

    public Task<Result<SyncQueue>> FindByIdAsync(Guid queueId) =>
        GetAsync<SyncQueue>($"repo/sync/{queueId}");

    public Task<Result<List<SyncQueue>>> GetPendingAsync(Guid userId, Guid deviceId) =>
        GetAsync<List<SyncQueue>>($"repo/sync/pending?userId={userId}&deviceId={deviceId}");

    public Task<Result<bool>> MarkProcessedAsync(Guid queueId) =>
        PostAsync<bool>($"repo/sync/{queueId}/processed", new { });

    public Task<Result<bool>> MarkFailedAsync(Guid queueId, string errorMessage) =>
        PostAsync<bool>($"repo/sync/{queueId}/failed", new { errorMessage });

    public Task<Result<SyncConflict>> LogConflictAsync(Guid userId, Guid deviceId, string fieldName, string expectedValue, string actualValue) =>
        PostAsync<SyncConflict>("repo/sync/conflicts", new { userId, deviceId, fieldName, expectedValue, actualValue });

    public Task<Result<List<SyncConflict>>> GetConflictsAsync(Guid userId) =>
        GetAsync<List<SyncConflict>>($"repo/sync/conflicts/{userId}");

    public Task<Result<bool>> ResolveConflictAsync(Guid conflictId, string resolution) =>
        PostAsync<bool>($"repo/sync/conflicts/{conflictId}/resolve", new { resolution });

    private async Task<Result<T>> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        return await ReadResult<T>(response);
    }

    private async Task<Result<T>> PostAsync<T>(string path, object body)
    {
        var response = await _http.PostAsJsonAsync(path, body);
        return await ReadResult<T>(response);
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
