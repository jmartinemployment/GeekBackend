using GeekSeo.Persistence.Entities;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Application.Services.Seo;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class ContentDocumentRepository(SeoDbContext db) : IContentDocumentRepository
{
    public async Task<Result<SeoContentDocument>> GetByIdAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        return doc is null
            ? Result<SeoContentDocument>.NotFound("Document not found")
            : Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<IReadOnlyList<SeoContentDocument>>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await db.ContentDocuments.AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoContentDocument>>.Success(list);
    }

    public async Task<Result<SeoContentDocument>> CreateAsync(
        Guid userId, CreateContentDocumentRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var parentId = request.ParentDocumentId is Guid pid && pid != Guid.Empty ? pid : (Guid?)null;
        var documentKind = ContentDocumentKindResolver.Resolve(request.DocumentKind, parentId);

        if (parentId is Guid parentDocumentId)
        {
            var parent = await db.ContentDocuments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == parentDocumentId, ct);
            if (parent is null)
                return Result<SeoContentDocument>.Failure("Parent document not found");
            if (parent.ProjectId != request.ProjectId)
                return Result<SeoContentDocument>.Failure("Parent document must belong to the same project");
        }

        string? publishSlug = null;
        if (!string.IsNullOrWhiteSpace(request.PublishSlug))
        {
            publishSlug = request.PublishSlug.Trim().ToLowerInvariant();
            if (!ContentPublishSlug.IsValid(publishSlug))
                return Result<SeoContentDocument>.Failure("Publish slug must be lowercase kebab-case.");

            var slugTaken = await db.ContentDocuments.AsNoTracking()
                .AnyAsync(d => d.ProjectId == request.ProjectId && d.PublishSlug == publishSlug, ct);
            if (slugTaken)
                return Result<SeoContentDocument>.Failure($"Publish slug \"{publishSlug}\" is already used in this project.");
        }

        string? spokeSourceType = null;
        if (!string.IsNullOrWhiteSpace(request.SpokeSourceType))
        {
            spokeSourceType = request.SpokeSourceType.Trim().ToLowerInvariant();
            if (!SpokeSourceTypes.IsKnown(spokeSourceType))
                return Result<SeoContentDocument>.Failure($"Unknown spoke source type \"{spokeSourceType}\".");
        }

        var doc = new SeoContentDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            UserId = userId,
            ParentDocumentId = parentId,
            DocumentKind = documentKind,
            PublishSlug = publishSlug,
            SpokeSourceType = spokeSourceType,
            SpokeSourcePhrase = string.IsNullOrWhiteSpace(request.SpokeSourcePhrase)
                ? null
                : request.SpokeSourcePhrase.Trim(),
            Title = request.Title,
            TargetKeyword = request.TargetKeyword,
            TargetLocation = request.TargetLocation,
            AnalysisRunId = request.AnalysisRunId,
            SerpKeyword = request.SerpKeyword,
            SiteProfileId = request.SiteProfileId,
            Status = "planned",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> UpdateContentAsync(
        Guid documentId, UpdateContentRequest request, int wordCount, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.ContentHtml = request.ContentHtml;
        doc.WordCount = wordCount;
        if (request.Title is not null) doc.Title = request.Title;
        if (request.TargetKeyword is not null) doc.TargetKeyword = request.TargetKeyword;
        if (request.TargetLocation is not null) doc.TargetLocation = request.TargetLocation;
        if (doc.Status == "planned")
            doc.Status = "writing";
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> UpdateStatusAsync(
        Guid documentId, string status, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.Status = status;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        if (status == "published")
        {
            doc.PublishedAt = DateTimeOffset.UtcNow;
            doc.PublishedScore = doc.SeoScore;
            doc.PublishedWordCount = doc.WordCount;
        }
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> AttachUrlResearchAsync(
        Guid documentId, Guid urlResearchId, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.UrlResearchId = urlResearchId;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> AttachAnalysisRunAsync(
        Guid documentId,
        Guid analysisRunId,
        string targetKeyword,
        string serpKeyword,
        Guid siteProfileId,
        CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.AnalysisRunId = analysisRunId;
        doc.TargetKeyword = targetKeyword;
        doc.SerpKeyword = serpKeyword;
        doc.SiteProfileId = siteProfileId;
        doc.SiteFocusJson = null;
        doc.SiteFocusCapturedAt = null;
        doc.KeywordBundleJson = null;
        doc.KeywordBundleCapturedAt = null;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> UpdateFeaturedImageAsync(
        Guid documentId, string featuredImageUrl, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.FeaturedImageUrl = featuredImageUrl;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result<SeoContentDocument>> UpdateBlogSpokeAsync(
        Guid documentId, string blogSpokeJson, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result<SeoContentDocument>.NotFound("Document not found");

        doc.BlogSpokeJson = blogSpokeJson;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentDocument>.Success(doc);
    }

    public async Task<Result> UpdateScoreAsync(
        Guid documentId, int score, string scoreComponentsJson, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result.Failure("Document not found");
        doc.SeoScore = score;
        doc.ScoreComponentsJson = scoreComponentsJson;
        doc.LastScoredAt = DateTimeOffset.UtcNow;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateAiDetectionScoreAsync(
        Guid documentId, decimal score, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result.Failure("Document not found");
        doc.AiDetectionScore = score;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.ContentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return Result.Failure("Document not found");
        db.ContentDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
