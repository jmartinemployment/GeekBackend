using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class ClientProfileRepository : IClientProfileRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ClientProfileRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ClientProfileDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ClientProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<ClientProfileDto?> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var entity = await _db.ClientProfiles.FirstOrDefaultAsync(p => p.ClientId == clientId, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<ClientProfileDto> CreateAsync(CreateClientProfileCommand command, CancellationToken ct = default)
    {
        var entity = new ClientProfile
        {
            ClientId = command.ClientId,
            Name = command.Name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ClientProfiles.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static ClientProfileDto MapToDto(ClientProfile entity) =>
        new(entity.Id, entity.ClientId, entity.Name, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}

public class ClientProfileVersionRepository : IClientProfileVersionRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ClientProfileVersionRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ClientProfileVersionDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ClientProfileVersions.FirstOrDefaultAsync(v => v.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ClientProfileVersionDto>> GetByProfileIdAsync(Guid profileId, CancellationToken ct = default)
    {
        var entities = await _db.ClientProfileVersions
            .Where(v => v.ProfileId == profileId)
            .OrderBy(v => v.Version)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ClientProfileVersionDto> CreateAsync(CreateClientProfileVersionCommand command, CancellationToken ct = default)
    {
        var profile = await _db.ClientProfiles.FirstOrDefaultAsync(p => p.Id == command.ProfileId, ct)
            ?? throw new KeyNotFoundException($"ClientProfile {command.ProfileId} not found");

        var latestVersion = await _db.ClientProfileVersions
            .Where(v => v.ProfileId == command.ProfileId)
            .MaxAsync(v => (int?)v.Version, ct) ?? 0;

        var entity = new ClientProfileVersion
        {
            ProfileId = command.ProfileId,
            Version = latestVersion + 1,
            ApprovedFacts = command.ApprovedFacts,
            ProhibitedClaims = command.ProhibitedClaims,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ClientProfileVersions.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static ClientProfileVersionDto MapToDto(ClientProfileVersion entity) =>
        new(entity.Id, entity.ProfileId, entity.Version, entity.ApprovedFacts,
            entity.ProhibitedClaims, entity.CreatedAtUtc, entity.RowVersion);
}

public class ClientBrandVoiceLinkRepository : IClientBrandVoiceLinkRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ClientBrandVoiceLinkRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ClientBrandVoiceLinkDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ClientBrandVoiceLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ClientBrandVoiceLinkDto>> GetByProfileVersionIdAsync(Guid profileVersionId, CancellationToken ct = default)
    {
        var entities = await _db.ClientBrandVoiceLinks
            .Where(l => l.ProfileVersionId == profileVersionId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ClientBrandVoiceLinkDto> CreateAsync(CreateClientBrandVoiceLinkCommand command, CancellationToken ct = default)
    {
        var entity = new ClientBrandVoiceLink
        {
            ProfileVersionId = command.ProfileVersionId,
            BrandVoiceId = command.BrandVoiceId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ClientBrandVoiceLinks.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static ClientBrandVoiceLinkDto MapToDto(ClientBrandVoiceLink entity) =>
        new(entity.Id, entity.ProfileVersionId, entity.BrandVoiceId, entity.CreatedAtUtc);
}
