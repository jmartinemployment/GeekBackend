using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class PainPointEvidenceLinkRepository : IPainPointEvidenceLinkRepository
{
    private readonly ContentWriterV3DbContext _db;

    public PainPointEvidenceLinkRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<PainPointEvidenceLinkDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.PainPointEvidenceLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<PainPointEvidenceLinkDto>> GetByPainPointIdAsync(Guid painPointId, CancellationToken ct = default)
    {
        var entities = await _db.PainPointEvidenceLinks
            .Where(l => l.PainPointId == painPointId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<PainPointEvidenceLinkDto> CreateAsync(CreatePainPointEvidenceLinkCommand command, CancellationToken ct = default)
    {
        var entity = new PainPointEvidenceLink
        {
            PainPointId = command.PainPointId,
            ResearchEvidenceId = command.ResearchEvidenceId,
            Dimension = command.Dimension,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PainPointEvidenceLinks.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static PainPointEvidenceLinkDto MapToDto(PainPointEvidenceLink entity) =>
        new(entity.Id, entity.PainPointId, entity.ResearchEvidenceId, entity.Dimension, entity.CreatedAtUtc);
}

public class ReconciliationProposalRepository : IReconciliationProposalRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ReconciliationProposalRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ReconciliationProposalDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ReconciliationProposals.FirstOrDefaultAsync(r => r.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ReconciliationProposalDto>> GetByResearchRunIdAsync(Guid researchRunId, CancellationToken ct = default)
    {
        var entities = await _db.ReconciliationProposals
            .Where(r => r.ResearchRunId == researchRunId && r.Status == "pending")
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ReconciliationProposalDto> CreateAsync(CreateReconciliationProposalCommand command, CancellationToken ct = default)
    {
        var entity = new ReconciliationProposal
        {
            ResearchRunId = command.ResearchRunId,
            ProposalType = command.ProposalType,
            PainPointId = command.PainPointId,
            ProposedData = command.ProposedData,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ReconciliationProposals.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ReconciliationProposalDto> ApproveAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await _db.ReconciliationProposals.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"ReconciliationProposal {id} not found");

        entity.Status = "approved";
        entity.ReviewedByUserId = userId;
        entity.ReviewedAtUtc = DateTime.UtcNow;

        _db.ReconciliationProposals.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<ReconciliationProposalDto> DismissAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await _db.ReconciliationProposals.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"ReconciliationProposal {id} not found");

        entity.Status = "dismissed";
        entity.ReviewedByUserId = userId;
        entity.ReviewedAtUtc = DateTime.UtcNow;

        _db.ReconciliationProposals.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ReconciliationProposalDto MapToDto(ReconciliationProposal entity) =>
        new(entity.Id, entity.ResearchRunId, entity.ProposalType, entity.PainPointId,
            entity.ProposedData, entity.Status, entity.ReviewedByUserId, entity.ReviewedAtUtc, entity.CreatedAtUtc);
}

public class KeywordCandidateRepository : IKeywordCandidateRepository
{
    private readonly ContentWriterV3DbContext _db;

    public KeywordCandidateRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<KeywordCandidateDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.KeywordCandidates.FirstOrDefaultAsync(k => k.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<KeywordCandidateDto>> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var entities = await _db.KeywordCandidates
            .Where(k => k.ClientId == clientId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<KeywordCandidateDto> CreateAsync(CreateKeywordCandidateCommand command, CancellationToken ct = default)
    {
        var entity = new KeywordCandidate
        {
            ClientId = command.ClientId,
            Keyword = command.Keyword,
            SearchVolume = command.SearchVolume,
            Difficulty = command.Difficulty,
            Intent = command.Intent,
            Status = "draft",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.KeywordCandidates.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<KeywordCandidateDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var entity = await _db.KeywordCandidates.FirstOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new KeyNotFoundException($"KeywordCandidate {id} not found");

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.KeywordCandidates.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static KeywordCandidateDto MapToDto(KeywordCandidate entity) =>
        new(entity.Id, entity.ClientId, entity.Keyword, entity.SearchVolume,
            entity.Difficulty, entity.Intent, entity.Status, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
