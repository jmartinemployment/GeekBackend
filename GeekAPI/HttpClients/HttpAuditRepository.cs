using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

public sealed class HttpAuditRepository : IAuditRepository
{
    private readonly HttpClient _http;

    public HttpAuditRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Result<bool>> CreateLogAsync(AuditLog log)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/audit/logs", new
        {
            userId = log.UserId,
            action = log.EventType,
            details = log.Description,
            ipAddress = log.IpAddress
        });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<bool>> CreateCircuitResetAsync(Guid userId, int failureCount)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/audit/circuit-resets", new { userId, failureCount });
        return await ReadResult<bool>(response);
    }

    public async Task<Result<AuditLog>> LogAsync(Guid userId, Guid? deviceId, string eventType, string description, string? ipAddress, string? userAgent, bool isSuccess, string? errorCode)
    {
        var log = new AuditLog { Id = Guid.NewGuid(), UserId = userId, DeviceId = deviceId, EventType = eventType, Description = description, IpAddress = ipAddress, UserAgent = userAgent, IsSuccess = isSuccess, ErrorCode = errorCode, CreatedAt = DateTime.UtcNow };
        var response = await _http.PostAsJsonAsync("repo/auth/audit/logs", new { userId, action = eventType, details = description, ipAddress });
        return await ReadResult<AuditLog>(response);
    }

    public async Task<Result<List<AuditLog>>> GetUserAuditLogsAsync(Guid userId, int skip = 0, int take = 50)
    {
        var response = await _http.GetAsync($"repo/auth/audit/logs/{userId}?skip={skip}&take={take}");
        return await ReadResult<List<AuditLog>>(response);
    }

    public async Task<Result<List<AuditLog>>> GetAuditLogsByEventTypeAsync(string eventType, int skip = 0, int take = 50)
    {
        var response = await _http.GetAsync($"repo/auth/audit/logs/by-event/{Uri.EscapeDataString(eventType)}?skip={skip}&take={take}");
        return await ReadResult<List<AuditLog>>(response);
    }

    public async Task<Result<List<AuditLog>>> GetRecentFailedLoginsAsync(Guid userId, int minutesBack = 30)
    {
        var response = await _http.GetAsync($"repo/auth/audit/logs/{userId}/failed-logins?minutes={minutesBack}");
        return await ReadResult<List<AuditLog>>(response);
    }

    public async Task<Result<SecurityIncident>> LogSecurityIncidentAsync(Guid userId, Guid? deviceId, string incidentType, string description, string? ipAddress, string? deviceFingerprint, string? evidence, bool requiresUserAction)
    {
        var response = await _http.PostAsJsonAsync("repo/auth/audit/incidents", new { userId, deviceId, incidentType, description, ipAddress, deviceFingerprint, evidence, requiresUserAction });
        return await ReadResult<SecurityIncident>(response);
    }

    public async Task<Result<List<SecurityIncident>>> GetUserSecurityIncidentsAsync(Guid userId, bool unresolvedOnly = false)
    {
        var response = await _http.GetAsync($"repo/auth/audit/incidents/{userId}?unresolvedOnly={unresolvedOnly}");
        return await ReadResult<List<SecurityIncident>>(response);
    }

    public async Task<Result<SecurityIncident?>> GetSecurityIncidentAsync(Guid incidentId)
    {
        var response = await _http.GetAsync($"repo/auth/audit/incidents/detail/{incidentId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return Result<SecurityIncident?>.Success(null);
        if (!response.IsSuccessStatusCode) return Result<SecurityIncident?>.Failure($"Repository HTTP error {(int)response.StatusCode}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<SecurityIncident>>();
        return Result<SecurityIncident?>.Success(wrapper?.Data);
    }

    public async Task<Result<bool>> ResolveSecurityIncidentAsync(Guid incidentId)
    {
        var response = await _http.PostAsync($"repo/auth/audit/incidents/{incidentId}/resolve", null);
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
