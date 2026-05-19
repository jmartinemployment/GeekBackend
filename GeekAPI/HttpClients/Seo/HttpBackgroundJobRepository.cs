using System.Net;
using System.Net.Http.Json;
using GeekAPI.Auth;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpBackgroundJobRepository(IHttpClientFactory factory, ICurrentUserContext user) : IBackgroundJobRepository
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public Task<Result<SeoBackgroundJob>> CreateAsync(CreateBackgroundJobRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("Create background jobs via GeekRepository only.");

    public async Task<Result<SeoBackgroundJob>> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/jobs/{jobId}?userId={user.UserId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<SeoBackgroundJob>.NotFound("Job not found");
        if (!response.IsSuccessStatusCode)
            return Result<SeoBackgroundJob>.Failure(await response.Content.ReadAsStringAsync(ct));
        var job = await response.Content.ReadFromJsonAsync<SeoBackgroundJob>(ct);
        return job is null ? Result<SeoBackgroundJob>.Failure("Empty response") : Result<SeoBackgroundJob>.Success(job);
    }

    public Task<Result> UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result> MarkCompleteAsync(Guid jobId, Guid? resultId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result> MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<SeoBackgroundJob>>> GetPendingAsync(string jobType, int limit, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
