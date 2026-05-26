using System.Text.RegularExpressions;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed partial class ContentDocumentService(IContentDocumentRepository documents, IProjectRepository projects)
    : IContentDocumentService
{
    public async Task<Result<SeoContentDocument>> EnsureAccessAsync(
        Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var docResult = await documents.GetByIdAsync(documentId, ct);
        if (!docResult.IsSuccess || docResult.Value is null)
            return Result<SeoContentDocument>.NotFound("Document not found");
        if (docResult.Value.UserId != userId)
            return Result<SeoContentDocument>.Failure("Access denied");
        return docResult;
    }

    public async Task<Result<IReadOnlyList<SeoContentDocument>>> ListByProjectAsync(
        Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<IReadOnlyList<SeoContentDocument>>.Failure("Access denied");
        return await documents.GetByProjectAsync(projectId, ct);
    }

    public Task<Result<SeoContentDocument>> GetAsync(Guid userId, Guid documentId, CancellationToken ct = default) =>
        EnsureAccessAsync(userId, documentId, ct);

    public async Task<Result<SeoContentDocument>> CreateAsync(
        Guid userId, CreateContentDocumentRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<SeoContentDocument>.Failure("Access denied");
        return await documents.CreateAsync(userId, request, ct);
    }

    public async Task<Result<SeoContentDocument>> UpdateContentAsync(
        Guid userId, Guid documentId, UpdateContentRequest request, CancellationToken ct = default)
    {
        var access = await EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess)
            return access;
        var wordCount = CountWords(request.ContentHtml);
        return await documents.UpdateContentAsync(documentId, request, wordCount, ct);
    }

    public async Task<Result<SeoContentDocument>> UpdateStatusAsync(
        Guid userId, Guid documentId, string status, CancellationToken ct = default)
    {
        var access = await EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess)
            return access;
        return await documents.UpdateStatusAsync(documentId, status, ct);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var access = await EnsureAccessAsync(userId, documentId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error ?? "Access denied");
        return await documents.DeleteAsync(documentId, ct);
    }

    private static int CountWords(string html)
    {
        var text = HtmlTagRegex().Replace(html, " ");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.None)]
    private static partial Regex HtmlTagRegex();
}
