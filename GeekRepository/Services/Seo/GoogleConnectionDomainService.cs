using GeekSeo.Application.Infrastructure;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Services.Seo;

/// <summary>
/// Shares Google OAuth connections across all projects with the same normalized site URL (per user).
/// Temporary until project-per-domain consolidation ships.
/// </summary>
internal static class GoogleConnectionDomainService
{
    internal static async Task<SeoProject?> GetOwnedProjectAsync(
        SeoDbContext db,
        Guid projectId,
        Guid userId,
        CancellationToken ct) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

    internal static async Task<IReadOnlyList<SeoProject>> GetSameDomainProjectsAsync(
        SeoDbContext db,
        SeoProject anchor,
        CancellationToken ct)
    {
        if (!SeoSiteUrlNormalizer.TryNormalize(anchor.Url, out var domainKey, out _))
            return [anchor];

        var projects = await db.Projects
            .Where(p => p.UserId == anchor.UserId)
            .ToListAsync(ct);

        return projects
            .Where(p => SeoSiteUrlNormalizer.TryNormalize(p.Url, out var normalized, out _)
                && string.Equals(normalized, domainKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    internal static async Task<SeoGscConnection?> FindGscForProjectAsync(
        SeoDbContext db,
        Guid projectId,
        Guid userId,
        CancellationToken ct)
    {
        var anchor = await GetOwnedProjectAsync(db, projectId, userId, ct);
        if (anchor is null)
            return null;

        var direct = await db.GscConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.UserId == userId, ct);
        if (direct is not null)
            return direct;

        var siblings = await GetSameDomainProjectsAsync(db, anchor, ct);
        var siblingIds = siblings.Select(p => p.Id).ToList();
        return await db.GscConnections.AsNoTracking()
            .Where(c => c.UserId == userId && siblingIds.Contains(c.ProjectId))
            .OrderByDescending(c => c.ConnectedAt)
            .FirstOrDefaultAsync(ct);
    }

    internal static async Task<SeoGa4Connection?> FindGa4ForProjectAsync(
        SeoDbContext db,
        Guid projectId,
        Guid userId,
        CancellationToken ct)
    {
        var anchor = await GetOwnedProjectAsync(db, projectId, userId, ct);
        if (anchor is null)
            return null;

        var direct = await db.Ga4Connections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (direct is not null)
            return direct;

        var siblings = await GetSameDomainProjectsAsync(db, anchor, ct);
        var siblingIds = siblings.Select(p => p.Id).ToList();
        return await db.Ga4Connections.AsNoTracking()
            .Where(c => siblingIds.Contains(c.ProjectId))
            .OrderByDescending(c => c.ConnectedAt)
            .FirstOrDefaultAsync(ct);
    }

    internal static async Task SyncGscToDomainAsync(
        SeoDbContext db,
        SeoGscConnection source,
        CancellationToken ct)
    {
        var anchor = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == source.ProjectId, ct);
        if (anchor is null)
            return;

        var siblings = await GetSameDomainProjectsAsync(db, anchor, ct);
        foreach (var sibling in siblings)
        {
            var trackedProject = await db.Projects.FirstOrDefaultAsync(p => p.Id == sibling.Id, ct);
            if (trackedProject is not null)
                trackedProject.GscConnected = true;

            var existing = await db.GscConnections
                .FirstOrDefaultAsync(c => c.ProjectId == sibling.Id, ct);
            if (existing is null)
            {
                db.GscConnections.Add(new SeoGscConnection
                {
                    Id = sibling.Id == source.ProjectId ? source.Id : Guid.NewGuid(),
                    ProjectId = sibling.Id,
                    UserId = source.UserId,
                    SiteUrl = source.SiteUrl,
                    EncryptedRefreshToken = source.EncryptedRefreshToken,
                    EncryptionIv = source.EncryptionIv,
                    EncryptionTag = source.EncryptionTag,
                    ConnectedAt = source.ConnectedAt,
                });
            }
            else
            {
                existing.UserId = source.UserId;
                existing.SiteUrl = source.SiteUrl;
                existing.EncryptedRefreshToken = source.EncryptedRefreshToken;
                existing.EncryptionIv = source.EncryptionIv;
                existing.EncryptionTag = source.EncryptionTag;
                existing.ConnectedAt = source.ConnectedAt;
            }
        }
    }

    internal static async Task SyncGa4ToDomainAsync(
        SeoDbContext db,
        SeoGa4Connection source,
        Guid userId,
        CancellationToken ct)
    {
        var anchor = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == source.ProjectId, ct);
        if (anchor is null)
            return;

        var siblings = await GetSameDomainProjectsAsync(db, anchor, ct);
        foreach (var sibling in siblings)
        {
            var existing = await db.Ga4Connections
                .FirstOrDefaultAsync(c => c.ProjectId == sibling.Id, ct);
            if (existing is null)
            {
                db.Ga4Connections.Add(new SeoGa4Connection
                {
                    Id = sibling.Id == source.ProjectId ? source.Id : Guid.NewGuid(),
                    ProjectId = sibling.Id,
                    PropertyId = source.PropertyId,
                    EncryptedRefreshToken = source.EncryptedRefreshToken,
                    EncryptionIv = source.EncryptionIv,
                    EncryptionTag = source.EncryptionTag,
                    ConnectedAt = source.ConnectedAt,
                });
            }
            else
            {
                existing.PropertyId = source.PropertyId;
                existing.EncryptedRefreshToken = source.EncryptedRefreshToken;
                existing.EncryptionIv = source.EncryptionIv;
                existing.EncryptionTag = source.EncryptionTag;
                existing.ConnectedAt = source.ConnectedAt;
            }
        }
    }

    internal static async Task DisconnectDomainAsync(
        SeoDbContext db,
        Guid projectId,
        Guid userId,
        CancellationToken ct)
    {
        var anchor = await GetOwnedProjectAsync(db, projectId, userId, ct);
        if (anchor is null)
            return;

        var siblings = await GetSameDomainProjectsAsync(db, anchor, ct);
        var siblingIds = siblings.Select(p => p.Id).ToList();

        var gscRows = await db.GscConnections
            .Where(c => siblingIds.Contains(c.ProjectId))
            .ToListAsync(ct);
        db.GscConnections.RemoveRange(gscRows);

        var ga4Rows = await db.Ga4Connections
            .Where(c => siblingIds.Contains(c.ProjectId))
            .ToListAsync(ct);
        db.Ga4Connections.RemoveRange(ga4Rows);

        foreach (var sibling in siblings)
        {
            var tracked = await db.Projects.FirstOrDefaultAsync(p => p.Id == sibling.Id, ct);
            if (tracked is not null)
                tracked.GscConnected = false;
        }
    }

    internal static void ApplyDomainGscFlags(IReadOnlyList<SeoProject> projects, IReadOnlyList<SeoGscConnection> connections)
    {
        if (projects.Count == 0 || connections.Count == 0)
            return;

        var projectById = projects.ToDictionary(p => p.Id);
        var connectedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in connections)
        {
            if (!projectById.TryGetValue(connection.ProjectId, out var project))
                continue;
            if (!SeoSiteUrlNormalizer.TryNormalize(project.Url, out var domainKey, out _))
                continue;
            connectedDomains.Add(domainKey);
        }

        foreach (var project in projects)
        {
            if (!SeoSiteUrlNormalizer.TryNormalize(project.Url, out var domainKey, out _))
                continue;
            if (connectedDomains.Contains(domainKey))
                project.GscConnected = true;
        }
    }
}
