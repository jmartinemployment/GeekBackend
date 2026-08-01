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

    public async Task<GccSiteAnalysisDto> CreateAsync(CreateGccSiteAnalysisCommand command, CancellationToken ct = default)
    {
        var e = new GccSiteAnalysis
        {
            Id = command.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            Domain = command.Domain.Trim(),
            SeedTopic = command.SeedTopic,
            GapsJson = command.GapsJson,
            IsDemo = command.IsDemo,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.GccSiteAnalyses.Add(e);
        await _db.SaveChangesAsync(ct);
        return Map(e);
    }

    private static GccSiteAnalysisDto Map(GccSiteAnalysis e) =>
        new(e.Id, e.Domain, e.SeedTopic, e.GapsJson, e.IsDemo, e.CreatedAtUtc);
}
