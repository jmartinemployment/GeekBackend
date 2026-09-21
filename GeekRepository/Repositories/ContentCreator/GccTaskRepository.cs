using System.Text.Json;
using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// Tasks and time, under a project.
/// </summary>
/// <remarks>
/// Every write pairs its change with a project log entry in one transaction, the same rule the
/// project repository follows.
///
/// The rate on a time entry is read from the client row inside the insert, never taken from the
/// caller and never resolved at read time. That is the difference between "what this cost" and
/// "what this would cost today", and only the first can be invoiced.
/// </remarks>
public class GccTaskRepository : IGccTaskRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccTaskRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<IReadOnlyList<GccTaskDto>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entities = await _db.GccTasks
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapTask).ToList().AsReadOnly();
    }

    public async Task<GccTaskDto?> CreateTaskAsync(
        CreateGccTaskCommand command,
        CancellationToken ct = default)
    {
        var projectExists = await _db.GccProjects.AnyAsync(p => p.Id == command.ProjectId, ct);
        if (!projectExists) return null;

        var now = DateTime.UtcNow;
        var entity = new GccTask
        {
            ProjectId = command.ProjectId,
            Name = command.Name.Trim(),
            Description = Normalize(command.Description),
            Status = GccTaskStatuses.Todo,
            AssigneeUserId = Normalize(command.AssigneeUserId),
            DueDate = command.DueDate,
            EstimatedHours = command.EstimatedHours,
            SortOrder = command.SortOrder,
            ContentTypes = CleanContentTypes(command.ContentTypes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccTasks.Add(entity);
        AddLog(command.ProjectId, command.ActorUserId, GccProjectLogEventTypes.TaskCreated, new
        {
            taskId = entity.Id,
            name = entity.Name,
            dueDate = entity.DueDate,
            assigneeUserId = entity.AssigneeUserId,
            contentTypes = entity.ContentTypes,
        }, now);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapTask(entity);
    }

    public async Task<GccTaskDto?> UpdateTaskAsync(
        UpdateGccTaskCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccTasks.FirstOrDefaultAsync(t => t.Id == command.Id, ct);
        if (entity is null) return null;

        var wasDone = entity.Status == GccTaskStatuses.Done;
        var now = DateTime.UtcNow;

        entity.Name = command.Name.Trim();
        entity.Description = Normalize(command.Description);
        entity.Status = command.Status;
        entity.AssigneeUserId = Normalize(command.AssigneeUserId);
        entity.DueDate = command.DueDate;
        entity.EstimatedHours = command.EstimatedHours;
        entity.SortOrder = command.SortOrder;
        entity.ContentTypes = CleanContentTypes(command.ContentTypes);
        entity.UpdatedAtUtc = now;

        // Completing a task is its own event. "updated" would bury the one transition anybody
        // reads the log to find.
        var becameDone = !wasDone && entity.Status == GccTaskStatuses.Done;
        var eventType = becameDone
            ? GccProjectLogEventTypes.TaskCompleted
            : GccProjectLogEventTypes.TaskUpdated;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccTasks.Update(entity);
        AddLog(entity.ProjectId, command.ActorUserId, eventType, new
        {
            taskId = entity.Id,
            name = entity.Name,
            status = entity.Status,
            dueDate = entity.DueDate,
            assigneeUserId = entity.AssigneeUserId,
            contentTypes = entity.ContentTypes,
        }, now);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapTask(entity);
    }

    public async Task<IReadOnlyList<GccTimeEntryDto>> ListTimeByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entities = await _db.GccTimeEntries
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);
        return entities.Select(MapEntry).ToList().AsReadOnly();
    }

    /// <summary>
    /// Log time against a project, and optionally one of its tasks.
    /// </summary>
    /// <remarks>
    /// Refusals are returned, not thrown, because each has something specific to say: a project
    /// that does not exist, or a billable entry against a client with no rate. The second is the
    /// point of leaving rate nullable on the client — it stops the billing rather than inventing a
    /// number to carry on with.
    /// </remarks>
    public async Task<GccTimeEntryResult> LogTimeAsync(
        CreateGccTimeEntryCommand command,
        CancellationToken ct = default)
    {
        var project = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct);
        if (project is null) return GccTimeEntryResult.Refused("That project does not exist.");

        var client = await _db.GccClients.FirstOrDefaultAsync(c => c.Id == project.ClientId, ct);
        if (client is null)
            return GccTimeEntryResult.Refused("That project's client does not exist.");

        decimal? rate = null;
        string? currency = null;
        if (command.Billable)
        {
            if (client.Rate is null)
            {
                return GccTimeEntryResult.Refused(
                    $"{client.Name} has no rate, so billable time cannot be logged against it. "
                    + "Set the client's rate first, or log this as non-billable.");
            }

            rate = client.Rate;
            currency = client.Currency;
        }

        var now = DateTime.UtcNow;
        var entity = new GccTimeEntry
        {
            ProjectId = command.ProjectId,
            TaskId = command.TaskId,
            UserId = command.UserId,
            WorkDate = command.WorkDate,
            Minutes = command.Minutes,
            Description = Normalize(command.Description),
            Billable = command.Billable,
            RateSnapshot = rate,
            Currency = currency,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.GccTimeEntries.Add(entity);
        AddLog(command.ProjectId, command.UserId, GccProjectLogEventTypes.TimeLogged, new
        {
            timeEntryId = entity.Id,
            taskId = entity.TaskId,
            workDate = entity.WorkDate,
            minutes = entity.Minutes,
            billable = entity.Billable,
            rateSnapshot = entity.RateSnapshot,
            currency = entity.Currency,
        }, now);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return GccTimeEntryResult.Logged(MapEntry(entity));
    }

    /// <summary>
    /// What a project's logged time adds up to.
    /// </summary>
    /// <remarks>
    /// Billable money is grouped by the currency each entry was logged in, never summed across
    /// them. A project whose client changed currency mid-flight has two real totals, and one
    /// number spanning both would be a mistake with a decimal point in it.
    /// </remarks>
    public async Task<GccProjectTimeTotals> TotalsForProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entries = await _db.GccTimeEntries
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(ct);

        var billable = entries
            .Where(e => e.Billable && e.RateSnapshot is not null && e.Currency is not null)
            .GroupBy(e => e.Currency!.Trim())
            .Select(g => new GccBillableTotal(
                g.Key,
                g.Sum(e => e.Minutes),
                // Rounded once, at the end of the currency's own sum. Rounding each entry first
                // would drift by a cent per line.
                Math.Round(g.Sum(e => e.Minutes / 60m * e.RateSnapshot!.Value), 2)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

        return new GccProjectTimeTotals(
            entries.Sum(e => e.Minutes),
            entries.Where(e => e.Billable).Sum(e => e.Minutes),
            billable.AsReadOnly());
    }

    private void AddLog(Guid projectId, string actor, string eventType, object payload, DateTime at) =>
        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = projectId,
            OccurredAtUtc = at,
            ActorUserId = actor,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
        });

    /// <summary>Trimmed, de-duplicated, blanks dropped. Order is not meaningful, so not preserved.</summary>
    private static List<string> CleanContentTypes(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.Select(v => v?.Trim() ?? string.Empty)
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GccTaskDto MapTask(GccTask entity) =>
        new(
            entity.Id,
            entity.ProjectId,
            entity.Name,
            entity.Description,
            entity.Status,
            entity.AssigneeUserId,
            entity.DueDate,
            entity.EstimatedHours,
            entity.SortOrder,
            entity.ContentTypes.AsReadOnly(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static GccTimeEntryDto MapEntry(GccTimeEntry entity) =>
        new(
            entity.Id,
            entity.ProjectId,
            entity.TaskId,
            entity.UserId,
            entity.WorkDate,
            entity.Minutes,
            entity.Description,
            entity.Billable,
            entity.RateSnapshot,
            entity.Currency?.Trim(),
            entity.InvoicedAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
