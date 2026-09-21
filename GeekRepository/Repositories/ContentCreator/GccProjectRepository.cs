using System.Text.Json;
using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// Projects and their log, in content_creator.
/// </summary>
/// <remarks>
/// Every write pairs its change with its log entry inside one transaction, so the two cannot come
/// apart. The constraints that matter — status values, the finished/finish-date pairing, the
/// budget/currency pairing, RESTRICT on every child, append-only on the log — live in the database
/// and are not restated here as C# checks that could drift from them.
/// </remarks>
public class GccProjectRepository : IGccProjectRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccProjectRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccProjectDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAtUtc == null, ct);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccProjectDto>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken ct = default)
    {
        var entities = await _db.GccProjects
            .Where(p => p.ClientId == clientId && p.DeletedAtUtc == null)
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    /// <summary>
    /// Create, or hand back what this idempotency key already created.
    /// </summary>
    /// <remarks>
    /// Insert first and let the unique index answer, rather than checking for the key and then
    /// inserting: two submits racing would both pass the check and one would still fail at the
    /// index. The index is the arbiter either way, so it is asked first.
    /// </remarks>
    public async Task<GccProjectCreateResult> CreateAsync(
        CreateGccProjectCommand command,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entity = new GccProject
        {
            ClientId = command.ClientId,
            IdempotencyKey = command.IdempotencyKey,
            Name = command.Name.Trim(),
            Code = Normalize(command.Code),
            Description = Normalize(command.Description),
            Status = GccProjectStatuses.Planned,
            SiteUrl = Normalize(command.SiteUrl),
            ProjectSiteRunId = command.ProjectSiteRunId,
            Department = Normalize(command.Department),
            PartnerUrls = CleanUrls(command.PartnerUrls),
            CompetitorUrls = CleanUrls(command.CompetitorUrls),
            StartDate = command.StartDate,
            DueDate = command.DueDate,
            FinishedDate = null,
            EstimatedHours = command.EstimatedHours,
            Budget = command.Budget,
            BudgetCurrency = Normalize(command.BudgetCurrency)?.ToUpperInvariant(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccProjects.Add(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = now,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.ProjectCreated,
            Payload = JsonSerializer.Serialize(new
            {
                name = entity.Name,
                clientId = entity.ClientId,
                startDate = entity.StartDate,
                dueDate = entity.DueDate,
                siteUrl = entity.SiteUrl,
                projectSiteRunId = entity.ProjectSiteRunId,
            }),
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return GccProjectCreateResult.Created(MapToDto(entity));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(ct);

            // The key has been used. Whose project it made decides what this is: the caller's own
            // double submit, or a key that belongs to someone else's row.
            _db.ChangeTracker.Clear();
            var existing = await _db.GccProjects
                .FirstOrDefaultAsync(p => p.IdempotencyKey == command.IdempotencyKey, ct);

            if (existing is null)
            {
                // The violation was some other unique index — the per-client code, in practice.
                // Not an idempotent repeat, and not this method's to reinterpret.
                return GccProjectCreateResult.ConflictingClient();
            }

            return existing.ClientId == command.ClientId
                ? GccProjectCreateResult.Existing(MapToDto(existing))
                : GccProjectCreateResult.ConflictingClient();
        }
    }

    public async Task<GccProjectDto?> UpdateAsync(
        UpdateGccProjectCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.Id && p.DeletedAtUtc == null, ct);
        if (entity is null) return null;

        var before = Snapshot(entity);

        entity.Name = command.Name.Trim();
        entity.Code = Normalize(command.Code);
        entity.Description = Normalize(command.Description);
        entity.SiteUrl = Normalize(command.SiteUrl);
        entity.ProjectSiteRunId = command.ProjectSiteRunId;
        entity.Department = Normalize(command.Department);
        entity.PartnerUrls = CleanUrls(command.PartnerUrls);
        entity.CompetitorUrls = CleanUrls(command.CompetitorUrls);
        entity.StartDate = command.StartDate;
        entity.DueDate = command.DueDate;
        entity.EstimatedHours = command.EstimatedHours;
        entity.Budget = command.Budget;
        entity.BudgetCurrency = Normalize(command.BudgetCurrency)?.ToUpperInvariant();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccProjects.Update(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = entity.UpdatedAtUtc,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.ProjectUpdated,
            Payload = JsonSerializer.Serialize(new { before, after = Snapshot(entity) }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapToDto(entity);
    }

    public async Task<GccProjectDto?> ChangeStatusAsync(
        ChangeGccProjectStatusCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.Id && p.DeletedAtUtc == null, ct);
        if (entity is null) return null;

        var from = entity.Status;
        entity.Status = command.Status;
        entity.FinishedDate = command.FinishedDate;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccProjects.Update(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = entity.UpdatedAtUtc,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.ProjectStatusChanged,
            Payload = JsonSerializer.Serialize(new
            {
                from,
                to = entity.Status,
                finishedDate = entity.FinishedDate,
            }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapToDto(entity);
    }

    /// <summary>
    /// Soft-delete: DeletedAtUtc is set and a project_deleted entry is logged, in one transaction.
    /// The row and its whole log stay exactly as they were — nothing here is a real DELETE.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, string actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAtUtc == null, ct);
        if (entity is null) return false;

        var now = DateTime.UtcNow;
        entity.DeletedAtUtc = now;
        entity.UpdatedAtUtc = now;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccProjects.Update(entity);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = now,
            ActorUserId = actorUserId,
            EventType = GccProjectLogEventTypes.ProjectDeleted,
            Payload = JsonSerializer.Serialize(new { name = entity.Name }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Delete one log entry, for real. The entry's own content does not survive this — what is left
    /// is the log_entry_deleted row written in its place, in the same transaction, naming who
    /// removed it and when. Idempotent in effect: a second call on a gone entry returns false, the
    /// same as one that never existed.
    /// </summary>
    public async Task<bool> DeleteLogEntryAsync(
        Guid projectId,
        long logEntryId,
        string actorUserId,
        CancellationToken ct = default)
    {
        var entry = await _db.GccProjectLog
            .FirstOrDefaultAsync(e => e.Id == logEntryId && e.ProjectId == projectId, ct);
        if (entry is null) return false;

        var deletedEventType = entry.EventType;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccProjectLog.Remove(entry);
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = projectId,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorUserId,
            EventType = GccProjectLogEventTypes.LogEntryDeleted,
            Payload = JsonSerializer.Serialize(new { deletedLogEntryId = logEntryId, deletedEventType }),
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<GccProjectLogEntryDto>> ListLogAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entries = await _db.GccProjectLog
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        return entries
            .Select(e => new GccProjectLogEntryDto(
                e.Id,
                e.ProjectId,
                e.OccurredAtUtc,
                e.ActorUserId,
                e.EventType,
                e.Payload))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>The fields a project_updated entry reports either side of the change.</summary>
    private static object Snapshot(GccProject entity) =>
        new
        {
            name = entity.Name,
            code = entity.Code,
            description = entity.Description,
            siteUrl = entity.SiteUrl,
            projectSiteRunId = entity.ProjectSiteRunId,
            department = entity.Department,
            partnerUrls = entity.PartnerUrls,
            competitorUrls = entity.CompetitorUrls,
            startDate = entity.StartDate,
            dueDate = entity.DueDate,
            estimatedHours = entity.EstimatedHours,
            budget = entity.Budget,
            budgetCurrency = entity.BudgetCurrency,
        };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Blank is absent. A column holding "" is a value nothing means to have stored.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> CleanUrls(IReadOnlyList<string>? urls) =>
        urls is null
            ? []
            : urls.Select(u => u?.Trim() ?? string.Empty)
                .Where(u => u.Length > 0)
                .ToList();

    private static GccProjectDto MapToDto(GccProject entity) =>
        new(
            entity.Id,
            entity.ClientId,
            entity.Name,
            entity.Code,
            entity.Description,
            entity.Status,
            entity.SiteUrl,
            entity.ProjectSiteRunId,
            entity.Department,
            entity.PartnerUrls.AsReadOnly(),
            entity.CompetitorUrls.AsReadOnly(),
            entity.StartDate,
            entity.DueDate,
            entity.FinishedDate,
            entity.EstimatedHours,
            entity.Budget,
            entity.BudgetCurrency?.Trim(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
