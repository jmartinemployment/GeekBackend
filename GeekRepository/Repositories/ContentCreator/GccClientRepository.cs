using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccClientRepository : IGccClientRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccClientRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccClientDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<GccClientDto?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var trimmedName = name.Trim();
        var entity = await _db.GccClients
            .FirstOrDefaultAsync(c => c.Name.ToLower() == trimmedName.ToLower(), ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<GccClientDto> CreateAsync(CreateGccClientCommand command, CancellationToken ct = default)
    {
        var entity = new GccClient
        {
            Name = command.Name.Trim(),
            Notes = command.Notes,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.GccClients.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static GccClientDto MapToDto(GccClient entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Notes,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
