using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly ContentWriterV3DbContext _db;

    public WorkspaceRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<WorkspaceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<WorkspaceDto> CreateAsync(CreateWorkspaceCommand command, CancellationToken ct = default)
    {
        var entity = new Workspace
        {
            Id = command.Id ?? Guid.NewGuid(),
            Name = command.Name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Workspaces.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<WorkspaceDto> UpdateAsync(UpdateWorkspaceCommand command, CancellationToken ct = default)
    {
        var entity = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Workspace {command.Id} not found");

        entity.Name = command.Name;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.Workspaces.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static WorkspaceDto MapToDto(Workspace entity) =>
        new(entity.Id, entity.Name, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}

public class ClientRepository : IClientRepository
{
    private readonly ContentWriterV3DbContext _db;

    public ClientRepository(ContentWriterV3DbContext db) => _db = db;

    public async Task<ClientDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ClientDto>> GetByWorkspaceIdAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var entities = await _db.Clients
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ClientDto> CreateAsync(CreateClientCommand command, CancellationToken ct = default)
    {
        var entity = new Client
        {
            WorkspaceId = command.WorkspaceId,
            Name = command.Name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Clients.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    private static ClientDto MapToDto(Client entity) =>
        new(entity.Id, entity.WorkspaceId, entity.Name, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
