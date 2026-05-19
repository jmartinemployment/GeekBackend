using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpAIWritingService(IHttpClientFactory factory) : IAIWritingService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<BackgroundJobStatus>> EnqueueFullArticleAsync(
        Guid userId, FullArticleRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/writing/full-article?userId={userId}", request, ct);
        if (!response.IsSuccessStatusCode)
            return Result<BackgroundJobStatus>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<BackgroundJobStatus>(ct);
        return value is null
            ? Result<BackgroundJobStatus>.Failure("Empty response")
            : Result<BackgroundJobStatus>.Success(value);
    }

    public Task<Result<WritingTextResult>> GenerateOutlineAsync(
        Guid userId, WritingOutlineRequest request, CancellationToken ct = default) =>
        PostWriting<WritingTextResult>(userId, "outline", request, ct);

    public Task<Result<WritingTextResult>> GenerateDraftAsync(
        Guid userId, WritingDraftRequest request, CancellationToken ct = default) =>
        PostWriting<WritingTextResult>(userId, "draft", request, ct);

    public Task<Result<WritingTextResult>> HumanizeAsync(
        Guid userId, HumanizeRequest request, CancellationToken ct = default) =>
        PostWriting<WritingTextResult>(userId, "humanize", request, ct);

    public Task<Result<AiDetectionResult>> DetectAsync(
        Guid userId, DetectAiRequest request, CancellationToken ct = default) =>
        PostWriting<AiDetectionResult>(userId, "detect", request, ct);

    private async Task<Result<T>> PostWriting<T>(Guid userId, string path, object body, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/writing/{path}?userId={userId}", body, ct);
        if (!response.IsSuccessStatusCode)
            return Result<T>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<T>(ct);
        return value is null ? Result<T>.Failure("Empty response") : Result<T>.Success(value);
    }
}
