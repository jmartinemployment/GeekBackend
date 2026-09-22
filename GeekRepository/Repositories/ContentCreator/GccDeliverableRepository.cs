using System.Text.Json;
using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// What each project has promised to hand over.
/// </summary>
/// <remarks>
/// The check that earns its keep here is the client match: a deliverable's create must belong to
/// the same client as its project. Nothing in the schema can express that — the create carries a
/// client id with no foreign key, and the project carries its own — so it is enforced in the same
/// transaction as the insert. Without it, one client's content becomes another client's
/// deliverable, which is the same defect as the keyword dedupe that started this work.
/// </remarks>
public class GccDeliverableRepository : IGccDeliverableRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccDeliverableRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<IReadOnlyList<GccDeliverableDto>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entities = await _db.GccDeliverables
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.DueDate ?? DateOnly.MaxValue)
            .ThenBy(d => d.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccDeliverableResult> CreateAsync(
        CreateGccDeliverableCommand command,
        CancellationToken ct = default)
    {
        var project = await _db.GccProjects.FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct);
        if (project is null) return GccDeliverableResult.Refused("That project does not exist.");

        var create = await _db.GccCreates.FirstOrDefaultAsync(c => c.Id == command.CreateId, ct);
        if (create is null) return GccDeliverableResult.Refused("That create does not exist.");

        if (create.ClientId != project.ClientId)
        {
            return GccDeliverableResult.Refused(
                "That create belongs to a different client than this project.");
        }

        var alreadyListed = await _db.GccDeliverables
            .AnyAsync(d => d.CreateId == command.CreateId, ct);
        if (alreadyListed)
        {
            // The unique index would catch this too. Saying it in words costs one query and saves
            // the operator a constraint-violation page.
            return GccDeliverableResult.Refused(
                "That create is already a deliverable on a project.");
        }

        var now = DateTime.UtcNow;
        var entity = new GccDeliverable
        {
            ProjectId = command.ProjectId,
            CreateId = command.CreateId,
            Name = command.Name.Trim(),
            // A deliverable's type is the create's type -- never a second, client-suppliable copy
            // that can drift from it. `create` is already loaded above for the ownership check.
            Type = create.StartingContentType,
            Status = GccDeliverableStatuses.Planned,
            DueDate = command.DueDate,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccDeliverables.Add(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.ProjectId,
            OccurredAtUtc = now,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.DeliverableCreated,
            Payload = JsonSerializer.Serialize(new
            {
                deliverableId = entity.Id,
                createId = entity.CreateId,
                name = entity.Name,
                type = entity.Type,
                dueDate = entity.DueDate,
            }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return GccDeliverableResult.Created(MapToDto(entity));
    }

    /// <summary>
    /// Move a deliverable to a new status.
    /// </summary>
    /// <remarks>
    /// Delivering stamps the moment, and any other status clears it — the database refuses the
    /// mismatched pair either way, so this sets both together rather than letting a caller
    /// assemble a row the constraint will reject.
    /// </remarks>
    public async Task<GccDeliverableDto?> ChangeStatusAsync(
        ChangeGccDeliverableStatusCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccDeliverables.FirstOrDefaultAsync(d => d.Id == command.Id, ct);
        if (entity is null) return null;

        var now = DateTime.UtcNow;
        var delivering = command.Status == GccDeliverableStatuses.Delivered;

        var from = entity.Status;
        entity.Status = command.Status;
        entity.DeliveredAtUtc = delivering ? entity.DeliveredAtUtc ?? now : null;
        entity.UpdatedAtUtc = now;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccDeliverables.Update(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.ProjectId,
            OccurredAtUtc = now,
            ActorUserId = command.ActorUserId,
            // Delivering is the transition anybody reads the log to find, so it is its own event.
            // Every other move is an update — never "created", which would be a plainly false
            // record of what happened.
            EventType = delivering
                ? GccProjectLogEventTypes.DeliverableDelivered
                : GccProjectLogEventTypes.DeliverableUpdated,
            Payload = JsonSerializer.Serialize(new
            {
                deliverableId = entity.Id,
                createId = entity.CreateId,
                from,
                to = entity.Status,
                deliveredAtUtc = entity.DeliveredAtUtc,
            }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapToDto(entity);
    }

    private static GccDeliverableDto MapToDto(GccDeliverable entity) =>
        new(
            entity.Id,
            entity.ProjectId,
            entity.CreateId,
            entity.Name,
            entity.Type,
            entity.Status,
            entity.DueDate,
            entity.DeliveredAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
