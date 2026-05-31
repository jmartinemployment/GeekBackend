using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class ContentGuardRepository(SeoDbContext db) : IContentGuardRepository
{
    public async Task<Result<SeoContentGuardPolicy?>> GetPolicyAsync(Guid projectId, CancellationToken ct = default)
    {
        var policy = await db.ContentGuardPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        return Result<SeoContentGuardPolicy?>.Success(policy);
    }

    public async Task<Result<SeoContentGuardPolicy>> UpsertPolicyAsync(
        SeoContentGuardPolicy policy,
        CancellationToken ct = default)
    {
        var existing = await db.ContentGuardPolicies.FirstOrDefaultAsync(p => p.ProjectId == policy.ProjectId, ct);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            policy.Id = policy.Id == Guid.Empty ? Guid.NewGuid() : policy.Id;
            policy.CreatedAt = now;
            policy.UpdatedAt = now;
            db.ContentGuardPolicies.Add(policy);
        }
        else
        {
            existing.Enabled = policy.Enabled;
            existing.AutoPatch = policy.AutoPatch;
            existing.UpdatedAt = now;
            policy = existing;
        }

        await db.SaveChangesAsync(ct);
        return Result<SeoContentGuardPolicy>.Success(policy);
    }

    public async Task<Result<IReadOnlyList<SeoContentGuardRun>>> ListRunsAsync(
        Guid projectId,
        int limit,
        CancellationToken ct = default)
    {
        var list = await db.ContentGuardRuns.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.DetectedAt)
            .Take(limit)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoContentGuardRun>>.Success(list);
    }

    public async Task<Result<SeoContentGuardRun>> CreateRunAsync(SeoContentGuardRun run, CancellationToken ct = default)
    {
        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;
        db.ContentGuardRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return Result<SeoContentGuardRun>.Success(run);
    }

    public async Task<Result<SeoContentGuardRun>> UpdateRunAsync(SeoContentGuardRun run, CancellationToken ct = default)
    {
        var existing = await db.ContentGuardRuns.FirstOrDefaultAsync(r => r.Id == run.Id, ct);
        if (existing is null)
            return Result<SeoContentGuardRun>.NotFound("Run not found");

        existing.Status = run.Status;
        existing.PrePatchHtml = run.PrePatchHtml;
        existing.PatchedHtml = run.PatchedHtml;
        existing.WordPressDraftPostId = run.WordPressDraftPostId;
        existing.Recommendation = run.Recommendation;
        existing.CompletedAt = run.CompletedAt;
        await db.SaveChangesAsync(ct);
        return Result<SeoContentGuardRun>.Success(existing);
    }

    public async Task<Result<SeoContentGuardRun?>> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.ContentGuardRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        return Result<SeoContentGuardRun?>.Success(run);
    }

    public async Task<Result<IReadOnlyList<ContentGuardScanCandidate>>> ListProjectsForDailyScanAsync(
        int limit,
        CancellationToken ct = default)
    {
        var list = await db.ContentGuardPolicies.AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.UpdatedAt)
            .Take(limit)
            .Select(p => new ContentGuardScanCandidate
            {
                ProjectId = p.ProjectId,
                UserId = p.UserId,
                AutoPatch = p.AutoPatch,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<ContentGuardScanCandidate>>.Success(list);
    }
}
