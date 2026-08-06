using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccSiteAnalysisRepository : IGccSiteAnalysisRepository
{
    private readonly ContentCreatorDbContext _db;
    public GccSiteAnalysisRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccSiteAnalysisDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.GccSiteAnalyses.FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? null : Map(e);
    }

    public async Task<GccSiteAnalysisDto?> GetLatestByDomainAsync(string domain, CancellationToken ct = default)
    {
        var e = await _db.GccSiteAnalyses
            .Where(x => x.Domain == domain)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return e is null ? null : Map(e);
    }

    public async Task<GccSiteAnalysisDto> CreateAsync(CreateGccSiteAnalysisCommand command, CancellationToken ct = default)
    {
        var e = new GccSiteAnalysis
        {
            Id = command.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            Domain = command.Domain.Trim(),
            SeedTopic = command.SeedTopic,
            GapsJson = command.GapsJson,
            Status = command.Status,
            SeoProjectId = command.SeoProjectId,
            SeoProfileId = command.SeoProfileId,
            ErrorMessage = command.ErrorMessage,
            SiteModelJson = command.SiteModelJson ?? """{"sitePages":[],"topicalNeighbors":[]}""",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _db.GccSiteAnalyses.Add(e);
        await _db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task<GccSiteAnalysisDto?> UpdateAsync(
        Guid id,
        UpdateGccSiteAnalysisCommand command,
        CancellationToken ct = default)
    {
        var e = await _db.GccSiteAnalyses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;

        e.Status = command.Status;
        e.SeoProjectId = command.SeoProjectId;
        e.SeoProfileId = command.SeoProfileId;
        e.ErrorMessage = command.ErrorMessage;
        if (command.GapsJson is not null) e.GapsJson = command.GapsJson;
        if (command.SiteModelJson is not null) e.SiteModelJson = command.SiteModelJson;
        e.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(e);
    }

    public async Task<IReadOnlyList<GccSiteFindingDto>> ReplaceFindingsAsync(
        Guid analysisId,
        CreateGccSiteFindingsCommand command,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.GccSiteFindings
            .Where(x => x.SiteAnalysisId == analysisId)
            .ExecuteDeleteAsync(ct);

        var now = DateTime.UtcNow;
        var entities = command.Findings.Select(f => new GccSiteFinding
        {
            Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
            SiteAnalysisId = analysisId,
            FindingType = f.FindingType.Trim(),
            Severity = f.Severity.Trim(),
            AffectedUrl = string.IsNullOrWhiteSpace(f.AffectedUrl) ? null : f.AffectedUrl.Trim(),
            Title = f.Title.Trim(),
            Summary = f.Summary.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(f.DetailsJson) ? "{}" : f.DetailsJson,
            CreatedAtUtc = f.CreatedAtUtc == default ? now : f.CreatedAtUtc,
        }).ToList();

        _db.GccSiteFindings.AddRange(entities);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return entities.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<GccSiteFindingDto>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken ct = default)
    {
        var findings = await _db.GccSiteFindings
            .AsNoTracking()
            .Where(x => x.SiteAnalysisId == analysisId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return findings.Select(Map).ToList();
    }

    private static GccSiteAnalysisDto Map(GccSiteAnalysis e) =>
        new(
            e.Id,
            e.Domain,
            e.SeedTopic,
            e.GapsJson,
            e.Status,
            e.SeoProjectId,
            e.SeoProfileId,
            e.ErrorMessage,
            e.SiteModelJson,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);

    private static GccSiteFindingDto Map(GccSiteFinding e) =>
        new(
            e.Id,
            e.SiteAnalysisId,
            e.FindingType,
            e.Severity,
            e.AffectedUrl,
            e.Title,
            e.Summary,
            e.DetailsJson,
            e.CreatedAtUtc);
}
