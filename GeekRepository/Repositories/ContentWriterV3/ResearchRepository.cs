using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class ResearchRunRepository : IResearchRunRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ResearchRunRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ResearchRunDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ResearchRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ResearchRunDto>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        var entities = await _db.ResearchRuns
            .Where(r => r.CampaignId == campaignId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ResearchRunDto> CreateAsync(CreateResearchRunCommand command, CancellationToken ct = default)
    {
        var entity = new ResearchRun
        {
            CampaignId = command.CampaignId,
            Keyword = command.Keyword,
            Status = "queued",
            MaxBudget = command.MaxBudget,
            SpentBudget = 0,
            DiscoveredSourceCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ResearchRuns.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ResearchRunDto> UpdateStatusAsync(UpdateResearchRunStatusCommand command, CancellationToken ct = default)
    {
        var entity = await _db.ResearchRuns.FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"ResearchRun {command.Id} not found");

        entity.Status = command.Status;
        entity.DiscoveredSourceCount = command.DiscoveredSourceCount;
        entity.SpentBudget = command.SpentBudget;
        entity.ErrorMessage = command.ErrorMessage;

        if (command.Status == "running" && entity.StartedAtUtc == null)
            entity.StartedAtUtc = DateTime.UtcNow;

        if (command.Status == "completed" || command.Status == "failed" && entity.CompletedAtUtc == null)
            entity.CompletedAtUtc = DateTime.UtcNow;

        _db.ResearchRuns.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ResearchRunDto MapToDto(ResearchRun entity) =>
        new(entity.Id, entity.CampaignId, entity.Keyword, entity.Status,
            entity.DiscoveredSourceCount, entity.SpentBudget, entity.MaxBudget,
            entity.ErrorMessage, entity.CreatedAtUtc, entity.StartedAtUtc, entity.CompletedAtUtc);
}

public class ResearchSourceRepository : IResearchSourceRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ResearchSourceRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ResearchSourceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ResearchSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ResearchSourceDto>> GetByResearchRunIdAsync(Guid researchRunId, CancellationToken ct = default)
    {
        var entities = await _db.ResearchSources
            .Where(s => s.ResearchRunId == researchRunId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ResearchSourceDto> CreateAsync(CreateResearchSourceCommand command, CancellationToken ct = default)
    {
        var entity = new ResearchSource
        {
            ResearchRunId = command.ResearchRunId,
            SourceType = command.SourceType,
            Url = command.Url,
            Title = command.Title,
            Description = command.Description,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ResearchSources.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ResearchSourceDto MapToDto(ResearchSource entity) =>
        new(entity.Id, entity.ResearchRunId, entity.SourceType, entity.Url,
            entity.Title, entity.Description, entity.CreatedAtUtc);
}

public class ResearchEvidenceRepository : IResearchEvidenceRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ResearchEvidenceRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ResearchEvidenceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ResearchEvidences.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ResearchEvidenceDto>> GetBySourceIdAsync(Guid sourceId, CancellationToken ct = default)
    {
        var entities = await _db.ResearchEvidences
            .Where(e => e.ResearchSourceId == sourceId)
            .OrderByDescending(e => e.Confidence)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ResearchEvidenceDto> CreateAsync(CreateResearchEvidenceCommand command, CancellationToken ct = default)
    {
        var entity = new ResearchEvidence
        {
            ResearchSourceId = command.ResearchSourceId,
            Statement = command.Statement,
            SupportLevel = command.SupportLevel,
            Confidence = command.Confidence,
            ApprovedForClaim = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ResearchEvidences.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ResearchEvidenceDto> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ResearchEvidences.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"ResearchEvidence {id} not found");

        entity.ApprovedForClaim = true;

        _db.ResearchEvidences.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ResearchEvidenceDto MapToDto(ResearchEvidence entity) =>
        new(entity.Id, entity.ResearchSourceId, entity.Statement, entity.SupportLevel,
            entity.ApprovedForClaim, entity.Confidence, entity.CreatedAtUtc);
}
