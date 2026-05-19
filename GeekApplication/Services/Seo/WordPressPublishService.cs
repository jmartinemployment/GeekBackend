using GeekApplication.Entities.Seo;
using GeekApplication.Infrastructure;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class WordPressPublishService(
    IProjectRepository projects,
    IContentDocumentService documents,
    IWordPressConnectionRepository connections,
    IWordPressProvider wordpress,
    IWordPressPublishRepository publishRepository) : IWordPressPublishService
{
    public async Task<Result> ConnectAsync(
        Guid userId, Guid projectId, WordPressConnectRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result.Failure("Access denied");

        var credentials = new WordPressCredentials
        {
            SiteUrl = request.SiteUrl,
            Username = request.Username,
            ApplicationPassword = request.ApplicationPassword,
        };

        var test = await wordpress.TestConnectionAsync(credentials, ct);
        if (!test.IsSuccess)
            return Result.Failure(test.Error ?? "WordPress connection test failed");

        var (cipher, iv, tag) = SeoCredentialProtector.Encrypt(request.ApplicationPassword);
        var row = new SeoWordPressConnection
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = userId,
            SiteUrl = request.SiteUrl.Trim().TrimEnd('/'),
            Username = request.Username,
            EncryptedAppPassword = cipher,
            EncryptionIv = iv,
            EncryptionTag = tag,
            DefaultPostStatus = request.DefaultPostStatus,
            ConnectedAt = DateTimeOffset.UtcNow,
        };

        var saved = await connections.UpsertAsync(row, ct);
        return saved.IsSuccess ? Result.Success() : Result.Failure(saved.Error ?? "Failed to save connection");
    }

    public async Task<Result<WordPressPublishResult>> PublishDocumentAsync(
        Guid userId, Guid documentId, WordPressPublishOptions options, CancellationToken ct = default)
    {
        var doc = await documents.GetAsync(userId, documentId, ct);
        if (!doc.IsSuccess || doc.Value is null)
            return Result<WordPressPublishResult>.Failure(doc.Error ?? "Document not found");

        var conn = await connections.GetByProjectAsync(doc.Value.ProjectId, ct);
        if (!conn.IsSuccess || conn.Value is null)
        {
            return Result<WordPressPublishResult>.Failure(
                "WordPress is not connected for this project. Connect in project settings first.");
        }

        var password = SeoCredentialProtector.Decrypt(
            conn.Value.EncryptedAppPassword, conn.Value.EncryptionIv, conn.Value.EncryptionTag);

        var credentials = new WordPressCredentials
        {
            SiteUrl = conn.Value.SiteUrl,
            Username = conn.Value.Username,
            ApplicationPassword = password,
        };

        var status = string.IsNullOrWhiteSpace(options.PostStatus)
            ? conn.Value.DefaultPostStatus
            : options.PostStatus;

        var published = await wordpress.PublishPostAsync(credentials, new WordPressPostPayload
        {
            Title = doc.Value.Title,
            ContentHtml = doc.Value.ContentHtml,
            Status = status,
            Slug = options.Slug,
        }, ct);

        if (!published.IsSuccess || published.Value is null)
            return Result<WordPressPublishResult>.Failure(published.Error ?? "Publish failed");

        await documents.UpdateStatusAsync(
            userId, documentId, status == "publish" ? "published" : "review", ct);

        var recorded = await publishRepository.RecordPublishAsync(
            doc.Value.ProjectId,
            documentId,
            doc.Value.TargetKeyword,
            doc.Value.WordCount,
            doc.Value.Title,
            published.Value.Link,
            published.Value.PostId,
            ct);

        if (!recorded.IsSuccess)
            return Result<WordPressPublishResult>.Failure(recorded.Error ?? "Failed to record publish");

        return Result<WordPressPublishResult>.Success(new WordPressPublishResult
        {
            PostId = published.Value.PostId,
            Url = published.Value.Link,
            Status = published.Value.Status,
        });
    }

    public async Task<Result> DisconnectAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result.Failure("Access denied");

        return await connections.DeleteByProjectAsync(projectId, ct);
    }

    public async Task<Result<WordPressConnectionStatus>> GetStatusAsync(
        Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<WordPressConnectionStatus>.Failure("Access denied");

        var conn = await connections.GetByProjectAsync(projectId, ct);
        if (!conn.IsSuccess)
            return Result<WordPressConnectionStatus>.Failure(conn.Error ?? "Failed to load connection");

        if (conn.Value is null)
            return Result<WordPressConnectionStatus>.Success(new WordPressConnectionStatus { Connected = false });

        return Result<WordPressConnectionStatus>.Success(new WordPressConnectionStatus
        {
            Connected = true,
            SiteUrl = conn.Value.SiteUrl,
            Username = conn.Value.Username,
            DefaultPostStatus = conn.Value.DefaultPostStatus,
        });
    }
}
