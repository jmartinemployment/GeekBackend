using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IWordPressPublishRepository
{
    Task<Result> RecordPublishAsync(
        Guid projectId,
        Guid documentId,
        string targetKeyword,
        int wordCount,
        string title,
        string publishedUrl,
        int wordPressPostId,
        CancellationToken ct = default);
}
