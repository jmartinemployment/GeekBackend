using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IAIWritingService
{
    Task<Result<BackgroundJobStatus>> EnqueueFullArticleAsync(
        Guid userId, FullArticleRequest request, CancellationToken ct = default);

    Task<Result<BackgroundJobStatus>> EnqueueBulkArticlesAsync(
        Guid userId, BulkArticleRequest request, CancellationToken ct = default);

    Task<Result<WritingTextResult>> GenerateOutlineAsync(
        Guid userId, WritingOutlineRequest request, CancellationToken ct = default);

    Task<Result<WritingTextResult>> GenerateDraftAsync(
        Guid userId, WritingDraftRequest request, CancellationToken ct = default);

    Task<Result<WritingTextResult>> HumanizeAsync(
        Guid userId, HumanizeRequest request, CancellationToken ct = default);

    Task<Result<AiDetectionResult>> DetectAsync(
        Guid userId, DetectAiRequest request, CancellationToken ct = default);
}
