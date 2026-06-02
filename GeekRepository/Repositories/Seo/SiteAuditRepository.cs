using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteAuditRepository(SeoDbContext db) : ISiteAuditRepository
{
    public async Task<Result<SeoSiteAudit>> CreateAsync(SeoSiteAudit audit, CancellationToken ct = default)
    {
        db.SiteAudits.Add(audit);
        await db.SaveChangesAsync(ct);
        return Result<SeoSiteAudit>.Success(audit);
    }

    public async Task<Result<SeoSiteAudit>> GetByIdAsync(Guid auditId, CancellationToken ct = default)
    {
        var audit = await db.SiteAudits.AsNoTracking()
            .Include(a => a.Pages)
            .FirstOrDefaultAsync(a => a.Id == auditId, ct);
        if (audit is null)
            return Result<SeoSiteAudit>.NotFound("Site audit not found");

        foreach (var page in audit.Pages)
            page.SiteAudit = null;

        return Result<SeoSiteAudit>.Success(audit);
    }

    public async Task<Result<IReadOnlyList<SeoSiteAudit>>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await db.SiteAudits.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.StartedAt)
            .Take(20)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoSiteAudit>>.Success(list);
    }

    public async Task<Result> UpdateStatusAsync(Guid auditId, UpdateSiteAuditStatusRequest request, CancellationToken ct = default)
    {
        var audit = await db.SiteAudits.FirstOrDefaultAsync(a => a.Id == auditId, ct);
        if (audit is null)
            return Result.Failure("Site audit not found");

        audit.Status = request.Status;
        audit.PagesCrawled = request.PagesCrawled;
        audit.OverallScore = request.OverallScore;
        audit.ErrorMessage = request.ErrorMessage;
        audit.CompletedAt = request.CompletedAt;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AppendPagesAsync(Guid auditId, AppendSiteAuditPagesRequest request, CancellationToken ct = default)
    {
        var audit = await db.SiteAudits.FirstOrDefaultAsync(a => a.Id == auditId, ct);
        if (audit is null)
            return Result.Failure("Site audit not found");

        foreach (var page in request.Pages)
        {
            db.SiteAuditPages.Add(new SeoSiteAuditPage
            {
                Id = Guid.NewGuid(),
                SiteAuditId = auditId,
                Url = page.Url,
                Score = page.Score,
                IssuesJson = page.IssuesJson,
                CrawledAt = page.CrawledAt,
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
