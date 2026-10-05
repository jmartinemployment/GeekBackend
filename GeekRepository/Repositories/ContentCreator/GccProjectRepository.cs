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
        return entity is null ? null : MapToDto(entity, await BriefSavedAtAsync(entity.Id, ct));
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

        var ids = entities.Select(p => p.Id).ToList();
        var savedAt = await _db.GccProjectRevisions
            .Where(r => ids.Contains(r.ProjectId))
            .GroupBy(r => r.ProjectId)
            .Select(g => new { ProjectId = g.Key, SavedAtUtc = g.Max(r => r.SavedAtUtc) })
            .ToDictionaryAsync(x => x.ProjectId, x => x.SavedAtUtc, ct);

        return entities
            .Select(p => MapToDto(p, savedAt.TryGetValue(p.Id, out var at) ? at : null))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>When the project's current brief was saved: its latest revision. Null before the first.</summary>
    private async Task<DateTime?> BriefSavedAtAsync(Guid projectId, CancellationToken ct) =>
        await _db.GccProjectRevisions
            .Where(r => r.ProjectId == projectId)
            .MaxAsync(r => (DateTime?)r.SavedAtUtc, ct);

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

    /// <remarks>
    /// The entity is tracked and never passed to <c>Update</c>, which marks every column modified: a
    /// Profile save rewrote brief_json and research_json from whatever this request had loaded. Only
    /// the columns set here are written. The row version (xmin) is in the UPDATE's WHERE, so a brief
    /// save landing between this read and this save makes it affect nothing, and it is refused.
    /// </remarks>
    public async Task<GccProjectWriteResult> UpdateAsync(
        UpdateGccProjectCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.Id && p.DeletedAtUtc == null, ct);
        if (entity is null) return GccProjectWriteResult.Missing();

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

        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = entity.UpdatedAtUtc,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.ProjectUpdated,
            Payload = JsonSerializer.Serialize(new { before, after = Snapshot(entity) }),
        });

        return await SaveOrRefuseAsync(entity, ct);
    }

    public async Task<GccProjectWriteResult> SetSiteRunAsync(
        SetGccProjectSiteRunCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.ProjectId && p.DeletedAtUtc == null, ct);
        if (entity is null) return GccProjectWriteResult.Missing();
        if (entity.ProjectSiteRunId == command.ProjectSiteRunId)
            return GccProjectWriteResult.Written(MapToDto(entity, await BriefSavedAtAsync(entity.Id, ct)));

        var before = Snapshot(entity);
        entity.ProjectSiteRunId = command.ProjectSiteRunId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = entity.UpdatedAtUtc,
            ActorUserId = command.ActorUserId,
            EventType = GccProjectLogEventTypes.ProjectUpdated,
            Payload = JsonSerializer.Serialize(new { before, after = Snapshot(entity) }),
        });

        return await SaveOrRefuseAsync(entity, ct);
    }

    /// <summary>
    /// The project and its log entry in one transaction, or nothing when the row changed since it was
    /// read. Not retried: a second attempt would be writing over a change this request never saw.
    /// </summary>
    private async Task<GccProjectWriteResult> SaveOrRefuseAsync(GccProject entity, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return GccProjectWriteResult.Conflict();
        }

        await transaction.CommitAsync(ct);
        return GccProjectWriteResult.Written(MapToDto(entity, await BriefSavedAtAsync(entity.Id, ct)));
    }

    /// <remarks>
    /// The brief version the editor read is made brief_version's original value, so the UPDATE's WHERE
    /// carries it: a save from an older read affects nothing and is refused, and so is one that loses
    /// the race to another brief save between this read and this save. Checked before the write as
    /// well, so a stale save does not reach the database at all. A write that saves increments it.
    ///
    /// Only the brief columns change. The revision is added in the same SaveChanges, so the project's
    /// brief and its latest revision are written together or not at all. Every save adds a row,
    /// except one identical to the newest revision: that writes nothing and returns the newest, so a
    /// Save that changes nothing stores nothing (J9). A blank topic leaves the keyword as it is.
    /// </remarks>
    public async Task<GccProjectBriefSaveResult> SaveBriefAsync(
        SaveGccProjectBriefCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.ProjectId && p.DeletedAtUtc == null, ct);
        if (entity is null) return GccProjectBriefSaveResult.Missing();
        if (entity.BriefVersion != command.ExpectedVersion) return GccProjectBriefSaveResult.Conflict();

        var topic = Normalize(command.Topic) ?? entity.Topic;
        var newest = await _db.GccProjectRevisions
            .Where(r => r.ProjectId == entity.Id)
            .OrderByDescending(r => r.SavedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (newest is not null && newest.BriefJson == command.BriefJson && newest.Topic == topic)
            return GccProjectBriefSaveResult.Saved(MapToDto(entity, newest.SavedAtUtc), MapRevision(newest));

        _db.Entry(entity).Property(p => p.BriefVersion).OriginalValue = command.ExpectedVersion;

        var now = DateTime.UtcNow;
        entity.BriefVersion = command.ExpectedVersion + 1;
        entity.BriefJson = command.BriefJson;
        entity.Topic = topic;
        entity.UpdatedAtUtc = now;

        var revision = new GccProjectRevision
        {
            ProjectId = entity.Id,
            Kind = GccProjectRevisionKinds.Manual,
            BriefJson = entity.BriefJson,
            Topic = entity.Topic,
            SavedBy = command.ActorUserId,
            SavedAtUtc = now,
        };
        _db.GccProjectRevisions.Add(revision);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return GccProjectBriefSaveResult.Conflict();
        }

        await transaction.CommitAsync(ct);
        return GccProjectBriefSaveResult.Saved(MapToDto(entity, revision.SavedAtUtc), MapRevision(revision));
    }

    private static GccProjectRevisionDto MapRevision(GccProjectRevision revision) =>
        new(
            revision.Id,
            revision.ProjectId,
            revision.Kind,
            revision.BriefJson,
            revision.Topic,
            revision.SavedBy,
            revision.SavedAtUtc);

    public async Task<GccProjectWriteResult> ChangeStatusAsync(
        ChangeGccProjectStatusCommand command,
        CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == command.Id && p.DeletedAtUtc == null, ct);
        if (entity is null) return GccProjectWriteResult.Missing();

        var from = entity.Status;
        entity.Status = command.Status;
        entity.FinishedDate = command.FinishedDate;
        entity.UpdatedAtUtc = DateTime.UtcNow;

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

        return await SaveOrRefuseAsync(entity, ct);
    }

    /// <summary>
    /// Soft-delete: DeletedAtUtc is set and a project_deleted entry is logged, in one transaction.
    /// The row and its whole log stay exactly as they were — nothing here is a real DELETE.
    /// </summary>
    public async Task<GccProjectWriteResult> DeleteAsync(Guid id, string actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.GccProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAtUtc == null, ct);
        if (entity is null) return GccProjectWriteResult.Missing();

        var now = DateTime.UtcNow;
        entity.DeletedAtUtc = now;
        entity.UpdatedAtUtc = now;

        _db.GccProjectLog.Add(new GccProjectLogEntry
        {
            ProjectId = entity.Id,
            OccurredAtUtc = now,
            ActorUserId = actorUserId,
            EventType = GccProjectLogEventTypes.ProjectDeleted,
            Payload = JsonSerializer.Serialize(new { name = entity.Name }),
        });

        var written = await SaveOrRefuseAsync(entity, ct);
        return written.Stale ? written : GccProjectWriteResult.Written(null);
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

    private static GccProjectDto MapToDto(GccProject entity, DateTime? briefSavedAtUtc = null) =>
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
            entity.UpdatedAtUtc,
            entity.BriefJson,
            entity.Topic,
            entity.ResearchJson,
            entity.SiteSectionJson,
            entity.BriefVersion,
            briefSavedAtUtc);
}
