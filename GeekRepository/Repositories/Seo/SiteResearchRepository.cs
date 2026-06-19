using System.Text.Json;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteResearchRepository(SeoDbContext db) : ISiteResearchRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<SeoSiteResearch>> GetOrCreateForProjectAsync(
        Guid userId, CreateSiteResearchRequest request, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);
        if (project is null)
            return Result<SeoSiteResearch>.Failure("Access denied");

        var siteUrl = request.SiteUrl.Trim().TrimEnd('/');
        var existing = await db.SiteResearch
            .FirstOrDefaultAsync(r => r.ProjectId == request.ProjectId && r.UserId == userId, ct);

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            if (!string.Equals(existing.SiteUrl, siteUrl, StringComparison.OrdinalIgnoreCase))
            {
                existing.SiteUrl = siteUrl;
                existing.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return Result<SeoSiteResearch>.Success(existing);
        }

        var row = new SeoSiteResearch
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            UserId = userId,
            SiteUrl = siteUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SiteResearch.Add(row);
        await db.SaveChangesAsync(ct);
        return Result<SeoSiteResearch>.Success(row);
    }

    public async Task<Result<SeoSiteResearch>> GetWithPagesAsync(Guid siteResearchId, CancellationToken ct = default)
    {
        var row = await db.SiteResearch.AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Pages.OrderBy(p => p.Url))
            .FirstOrDefaultAsync(r => r.Id == siteResearchId, ct);

        return row is null
            ? Result<SeoSiteResearch>.NotFound("Site research not found")
            : Result<SeoSiteResearch>.Success(row);
    }

    public async Task<Result<SeoSiteResearch>> PersistStep1Async(
        Guid siteResearchId, SiteResearchStep1Write body, CancellationToken ct = default)
    {
        var row = await db.SiteResearch.FirstOrDefaultAsync(r => r.Id == siteResearchId, ct);
        if (row is null)
            return Result<SeoSiteResearch>.NotFound("Site research not found");

        row.DiscoveredUrlsJson = JsonSerializer.Serialize(body.DiscoveredUrls, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoSiteResearch>.Success(row);
    }

    public async Task<Result<SeoSiteResearch>> ReplacePagesAsync(
        Guid siteResearchId, IReadOnlyList<SiteResearchPageWrite> pages, CancellationToken ct = default)
    {
        var row = await db.SiteResearch
            .Include(r => r.Pages)
            .FirstOrDefaultAsync(r => r.Id == siteResearchId, ct);
        if (row is null)
            return Result<SeoSiteResearch>.NotFound("Site research not found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.SiteResearchPages.RemoveRange(row.Pages);
            var now = DateTimeOffset.UtcNow;
            foreach (var page in pages)
            {
                db.SiteResearchPages.Add(new SeoSiteResearchPage
                {
                    Id = Guid.NewGuid(),
                    SiteResearchId = siteResearchId,
                    Url = page.Url.Trim(),
                    Html = page.Html,
                    HeadingsJson = page.HeadingsJson,
                    JsonLdJson = page.JsonLdJson,
                    ExtractSuccess = page.ExtractSuccess,
                    ExtractError = page.ExtractError,
                    UpdatedAt = now,
                });
            }

            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetWithPagesAsync(siteResearchId, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result<SeoSiteResearch>.Failure($"Failed to replace site pages: {ex.Message}");
        }
    }

    public async Task<Result<SeoSiteResearch>> PersistStep4Async(
        Guid siteResearchId, SiteResearchStep4Write body, CancellationToken ct = default)
    {
        var row = await db.SiteResearch.FirstOrDefaultAsync(r => r.Id == siteResearchId, ct);
        if (row is null)
            return Result<SeoSiteResearch>.NotFound("Site research not found");

        row.BusinessSummary = body.BusinessSummary;
        row.InternalLinkMapJson = body.InternalLinkMapJson;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoSiteResearch>.Success(row);
    }

    public async Task<Result> UpsertStepRunAsync(SiteAnalyzerStepRunUpsert upsert, CancellationToken ct = default)
    {
        if (upsert.SiteResearchId is null && upsert.UrlResearchId is null)
            return Result.Failure("SiteResearchId or UrlResearchId is required.");

        // Pack steps (5–10) send both IDs; keyword-pack rows must key on urlResearchId.
        SeoSiteAnalyzerStepRun? existing = upsert.UrlResearchId is Guid packId
            ? await db.SiteAnalyzerStepRuns.FirstOrDefaultAsync(
                r => r.UrlResearchId == packId && r.StepNumber == upsert.StepNumber, ct)
            : upsert.SiteResearchId is Guid siteId
                ? await db.SiteAnalyzerStepRuns.FirstOrDefaultAsync(
                    r => r.SiteResearchId == siteId
                        && r.UrlResearchId == null
                        && r.StepNumber == upsert.StepNumber, ct)
                : null;

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new SeoSiteAnalyzerStepRun
            {
                Id = Guid.NewGuid(),
                SiteResearchId = upsert.SiteResearchId,
                UrlResearchId = upsert.UrlResearchId,
                StepNumber = upsert.StepNumber,
            };
            db.SiteAnalyzerStepRuns.Add(existing);
        }
        else if (upsert.SiteResearchId is Guid linkedSiteId)
        {
            existing.SiteResearchId = linkedSiteId;
        }

        existing.Status = upsert.Status;
        existing.Message = upsert.Message;
        existing.Log = upsert.Log;
        existing.CountsJson = upsert.CountsJson;
        existing.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalyzerStepRunRow>>> GetStepRunsForSiteAsync(
        Guid siteResearchId, CancellationToken ct = default)
    {
        var rows = await db.SiteAnalyzerStepRuns.AsNoTracking()
            .Where(r => r.SiteResearchId == siteResearchId && r.UrlResearchId == null)
            .OrderBy(r => r.StepNumber)
            .Select(r => new SiteAnalyzerStepRunRow
            {
                StepNumber = r.StepNumber,
                Status = r.Status,
                Message = r.Message,
                Log = r.Log,
                CountsJson = r.CountsJson,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalyzerStepRunRow>>.Success(rows);
    }

    public async Task<Result<IReadOnlyList<SiteAnalyzerStepRunRow>>> GetStepRunsForPackAsync(
        Guid urlResearchId, CancellationToken ct = default)
    {
        var rows = await db.SiteAnalyzerStepRuns.AsNoTracking()
            .Where(r => r.UrlResearchId == urlResearchId)
            .OrderBy(r => r.StepNumber)
            .Select(r => new SiteAnalyzerStepRunRow
            {
                StepNumber = r.StepNumber,
                Status = r.Status,
                Message = r.Message,
                Log = r.Log,
                CountsJson = r.CountsJson,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalyzerStepRunRow>>.Success(rows);
    }
}
