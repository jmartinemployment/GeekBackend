using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data;
using System.Text.Json;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.Crm;

/// <summary>
/// geek_crm in GeekRepository's own Postgres (docs/GEEK-CRM-DATA-MODEL.md). Every query is scoped by
/// tenant, and every operation runs in one transaction that tells the database its tenant
/// (<see cref="ICrmTenantScope"/>), so row-level security backs the filter and an operation's writes
/// land together or not at all. Every method answers a Result and nothing throws past it: a database
/// failure is store_unavailable, logged by operation and never with a person's details.
/// </summary>
public sealed partial class CrmRepository(CrmDbContext db, TimeProvider clock, ILogger<CrmRepository> logger, ICrmTenantScope scope) : ICrmStore, ICrmPlatformStore
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 200;
    private const int RecentActivityCount = 25;

    // ----- Receptionist -----

    public Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default) =>
        RunAsync("ensure_participant_by_phone", command.TenantId, async () =>
        {
            if (!CrmText.IsE164(command.PhoneE164))
                return Invalid<EnsureParticipantResult>("A phone number in E.164 form is required.");
            if (!ParticipantSources.All.Contains(command.Source))
                return Invalid<EnsureParticipantResult>($"Unknown source '{command.Source}'.");
            if (!await TenantExistsAsync(command.TenantId, ct))
                return NotFound<EnsureParticipantResult>("Unknown tenant.");

            var existing = await db.ContactPoints.AsNoTracking()
                .Where(c => c.TenantId == command.TenantId && c.Kind == ContactPointKinds.Phone && c.Value == command.PhoneE164)
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.CreatedAt)
                .Select(c => (Guid?)c.ParticipantId)
                .FirstOrDefaultAsync(ct);
            if (existing is { } found)
                return Result<EnsureParticipantResult>.Success(new EnsureParticipantResult(found, false));

            var now = Now();
            var participant = NewParticipant(command.TenantId, ParticipantKinds.Person, CrmText.FormatPhone(command.PhoneE164),
                command.Source, command.SourceDetail, null, now);
            db.Participants.Add(participant);
            db.Persons.Add(new CrmPersonRow { ParticipantId = participant.Id });
            db.ContactPoints.Add(NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Phone,
                ContactPointLabels.Mobile, command.PhoneE164, isPrimary: true, now));
            await db.SaveChangesAsync(ct);
            return Result<EnsureParticipantResult>.Success(new EnsureParticipantResult(participant.Id, true));
        }, ct);

    public Task<Result<RecordActivityResult>> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default) =>
        RunAsync("record_activity", command.TenantId, async () =>
        {
            if (!ActivityChannels.All.Contains(command.Channel))
                return Invalid<RecordActivityResult>($"Unknown channel '{command.Channel}'.");
            if (command.Direction is not null && !ActivityDirections.All.Contains(command.Direction))
                return Invalid<RecordActivityResult>($"Unknown direction '{command.Direction}'.");
            if (command.Outcome is not null && !ActivityOutcomes.All.Contains(command.Outcome))
                return Invalid<RecordActivityResult>($"Unknown outcome '{command.Outcome}'.");
            if (string.IsNullOrWhiteSpace(command.CreatedBy))
                return Invalid<RecordActivityResult>("createdBy is required.");
            if (command.Priority is not null && !ActivityPriorities.All.Contains(command.Priority))
                return Invalid<RecordActivityResult>($"Unknown priority '{command.Priority}'.");
            if (!await TenantExistsAsync(command.TenantId, ct))
                return NotFound<RecordActivityResult>("Unknown tenant.");
            if (command.ParticipantId is { } pid &&
                !await db.Participants.AnyAsync(p => p.Id == pid && p.TenantId == command.TenantId, ct))
                return NotFound<RecordActivityResult>("Unknown participant.");

            if (command.ExternalRef is not null && await FindActivityAsync(command, ct) is { } already)
                return Result<RecordActivityResult>.Success(new RecordActivityResult(already, false));

            var row = new CrmActivityRow
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                ParticipantId = command.ParticipantId,
                DealId = command.DealId,
                Channel = command.Channel,
                Direction = command.Direction,
                OccurredAt = command.OccurredAt.ToUniversalTime(),
                Subject = command.Subject,
                Body = command.Body,
                Summary = command.Summary,
                RecordingUrl = command.RecordingUrl,
                DurationSeconds = command.DurationSeconds,
                Outcome = command.Outcome,
                ExternalRef = command.ExternalRef,
                CreatedBy = command.CreatedBy,
                CreatedAt = Now(),
                Priority = command.Priority,
            };
            db.Activities.Add(row);
            // A unique violation aborts the whole transaction, so the insert is made behind a savepoint
            // that the race below rolls back to before it reads the other insert.
            var transaction = db.Database.CurrentTransaction;
            if (transaction is not null) await transaction.CreateSavepointAsync("record_activity", ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A retry raced this one in: the other insert is the activity.
                if (transaction is not null) await transaction.RollbackToSavepointAsync("record_activity", ct);
                db.ChangeTracker.Clear();
                if (await FindActivityAsync(command, ct) is { } raced)
                    return Result<RecordActivityResult>.Success(new RecordActivityResult(raced, false));
                throw;
            }

            if (command.ParticipantId is { } touched && await db.Participants.FirstOrDefaultAsync(p => p.Id == touched, ct) is { } participant)
            {
                participant.UpdatedAt = row.CreatedAt;
                await db.SaveChangesAsync(ct);
            }

            return Result<RecordActivityResult>.Success(new RecordActivityResult(row.Id, true));
        }, ct);

    public Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default) =>
        RunAsync("set_sms_opt_out", command.TenantId, async () =>
        {
            if (!CrmText.IsE164(command.PhoneE164))
                return Invalid<bool>("A phone number in E.164 form is required.");
            var updated = await db.ContactPoints
                .Where(c => c.TenantId == command.TenantId && c.Kind == ContactPointKinds.Phone && c.Value == command.PhoneE164)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SmsOptOut, command.OptedOut), ct);
            return Result<bool>.Success(updated > 0);
        }, ct);

    // ----- Import -----

    public Task<Result<ImportProspectsResult>> ImportProspectsAsync(ImportProspectsCommand command, CancellationToken ct = default) =>
        RunAsync("import_prospects", command.TenantId, async () =>
        {
            if (command.Prospects is null || command.Prospects.Count == 0)
                return Invalid<ImportProspectsResult>("No prospects to import.");
            if (command.Prospects.Count > 5000)
                return Invalid<ImportProspectsResult>("At most 5,000 prospects per import.");
            if (!await TenantExistsAsync(command.TenantId, ct))
                return NotFound<ImportProspectsResult>("Unknown tenant.");

            // The whole batch is the operation's one transaction: a failure partway leaves nothing behind,
            // and a dry run does exactly the work of a real run and is then rolled back (see discard below).
            var batchId = Guid.NewGuid();
            int people = 0, orgs = 0, matched = 0, skipped = 0;
            var problems = new List<string>();
            var now = Now();
            var row = 0;

            foreach (var p in command.Prospects)
            {
                row++;
                var hasPerson = !string.IsNullOrWhiteSpace(p.FirstName) || !string.IsNullOrWhiteSpace(p.LastName) || !string.IsNullOrWhiteSpace(p.Email);
                var hasCompany = !string.IsNullOrWhiteSpace(p.CompanyName);
                if (!hasPerson && !hasCompany)
                {
                    skipped++;
                    problems.Add($"Row {row}: no name, email or company; skipped.");
                    continue;
                }
                if (p.AnnualRevenueBand is not null && !RevenueBands.All.Contains(p.AnnualRevenueBand))
                {
                    problems.Add($"Row {row}: revenue band '{p.AnnualRevenueBand}' ignored.");
                }

                Guid? orgId = null;
                if (hasCompany)
                {
                    var (id, created) = await EnsureOrganizationAsync(command, p, now, batchId, ct);
                    orgId = id;
                    if (created) orgs++; else matched++;
                }

                if (hasPerson)
                {
                    var (personId, created) = await EnsurePersonAsync(command, p, now, batchId, ct);
                    if (created) people++; else matched++;
                    if (orgId is { } o)
                        await EnsureLinkAsync(command.TenantId, personId, o, CrmText.RoleFromTitle(p.PersonRoleAtCompany ?? p.JobTitle), now, ct);
                }

                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            return Result<ImportProspectsResult>.Success(new ImportProspectsResult(
                people, orgs, matched, skipped, problems, command.DryRun ? null : batchId, command.DryRun));
        }, ct, discard: result => result.DryRun);

    private async Task<(Guid Id, bool Created)> EnsureOrganizationAsync(ImportProspectsCommand command, ImportProspect p, DateTime now, Guid batchId, CancellationToken ct)
    {
        var name = p.CompanyName!.Trim();
        var domain = CrmText.Domain(p.CompanyWebsite);

        // The database narrows to organizations whose name matches or whose website mentions the domain;
        // the domain itself is then compared exactly, so acme.com never matches notacme.com.
        var candidates = await (
            from part in db.Participants
            join org in db.Organizations on part.Id equals org.ParticipantId
            where part.TenantId == command.TenantId && part.ArchivedAt == null
                  && (EF.Functions.ILike(org.LegalName, CrmRepository.EscapeLike(name))
                      || (domain != null && org.Website != null && EF.Functions.ILike(org.Website, "%" + CrmRepository.EscapeLike(domain) + "%")))
            select new { part.Id, org.LegalName, org.Website }).ToListAsync(ct);
        var existing = candidates.FirstOrDefault(c =>
            string.Equals(c.LegalName, name, StringComparison.OrdinalIgnoreCase) || CrmText.SameDomain(c.Website, p.CompanyWebsite));
        if (existing is not null) return (existing.Id, false);

        var participant = NewParticipant(command.TenantId, ParticipantKinds.Organization, name,
            ParticipantSources.Import, command.SourceDetail, null, now);
        db.Participants.Add(participant);
        db.Organizations.Add(new CrmOrganizationRow
        {
            ParticipantId = participant.Id,
            LegalName = name,
            NaicsCode = CrmText.FirstCode(p.NaicsCode, 6),
            SicCode = CrmText.FirstCode(p.SicCode, 4),
            IndustryLabel = CrmText.Trim(p.IndustryLabel),
            EmployeeCount = p.EmployeeCount,
            AnnualRevenueBand = p.AnnualRevenueBand is not null && RevenueBands.All.Contains(p.AnnualRevenueBand) ? p.AnnualRevenueBand : null,
            Website = CrmText.Trim(p.CompanyWebsite),
        });
        if (CrmText.IsE164(p.CompanyPhoneE164))
        {
            var phone = NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Phone, ContactPointLabels.Main, p.CompanyPhoneE164!, true, now);
            phone.DoNotContact = p.DoNotCall;
            db.ContactPoints.Add(phone);
        }
        if (!string.IsNullOrWhiteSpace(p.CompanyWebsite))
            db.ContactPoints.Add(NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Website, ContactPointLabels.Main, p.CompanyWebsite.Trim(), true, now));
        if (!string.IsNullOrWhiteSpace(p.CompanyAddressLine1) || !string.IsNullOrWhiteSpace(p.CompanyCity))
        {
            var addr = NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Address, ContactPointLabels.Main,
                string.Join(", ", new[] { p.CompanyAddressLine1, p.CompanyCity, p.CompanyRegion, p.CompanyPostalCode }.Where(s => !string.IsNullOrWhiteSpace(s))),
                true, now);
            addr.AddressLine1 = CrmText.Trim(p.CompanyAddressLine1);
            addr.City = CrmText.Trim(p.CompanyCity);
            addr.Region = CrmText.Trim(p.CompanyRegion);
            addr.PostalCode = CrmText.Trim(p.CompanyPostalCode);
            addr.Country = CrmText.Trim(p.CompanyCountry);
            db.ContactPoints.Add(addr);
        }
        StampBatch(batchId);
        await db.SaveChangesAsync(ct);
        return (participant.Id, true);
    }

    private async Task<(Guid Id, bool Created)> EnsurePersonAsync(ImportProspectsCommand command, ImportProspect p, DateTime now, Guid batchId, CancellationToken ct)
    {
        var email = CrmText.Email(p.Email);
        if (email is not null)
        {
            var byEmail = await db.ContactPoints.AsNoTracking()
                .Where(c => c.TenantId == command.TenantId && c.Kind == ContactPointKinds.Email && c.Value == email)
                .Select(c => (Guid?)c.ParticipantId).FirstOrDefaultAsync(ct);
            if (byEmail is { } e) return (e, false);
        }
        var externalId = CrmText.Trim(p.ExternalId);
        if (externalId is not null)
        {
            var byExternal = await db.Participants.AsNoTracking()
                .Where(x => x.TenantId == command.TenantId && x.Kind == ParticipantKinds.Person && x.ExternalId == externalId)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (byExternal is { } x) return (x, false);
        }

        var display = string.Join(' ', new[] { p.FirstName?.Trim(), p.LastName?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (display.Length == 0) display = email ?? "Unknown";

        var participant = NewParticipant(command.TenantId, ParticipantKinds.Person, display, ParticipantSources.Import, command.SourceDetail, externalId, now);
        db.Participants.Add(participant);
        db.Persons.Add(new CrmPersonRow
        {
            ParticipantId = participant.Id,
            FirstName = CrmText.Trim(p.FirstName),
            LastName = CrmText.Trim(p.LastName),
            JobTitle = CrmText.Trim(p.JobTitle),
        });
        if (email is not null)
            db.ContactPoints.Add(NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Email, ContactPointLabels.Work, email, true, now));
        var firstPhone = true;
        foreach (var phone in p.Phones ?? [])
        {
            if (!CrmText.IsE164(phone.E164)) continue;
            var label = ContactPointLabels.All.Contains(phone.Label) ? phone.Label : ContactPointLabels.Other;
            var cp = NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Phone, label, phone.E164, firstPhone, now);
            cp.DoNotContact = p.DoNotCall;
            db.ContactPoints.Add(cp);
            firstPhone = false;
        }
        if (!string.IsNullOrWhiteSpace(p.LinkedInUrl))
            db.ContactPoints.Add(NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Social, ContactPointLabels.Work, p.LinkedInUrl.Trim(), false, now));
        if (!string.IsNullOrWhiteSpace(p.City) || !string.IsNullOrWhiteSpace(p.Region))
        {
            var addr = NewContactPoint(command.TenantId, participant.Id, ContactPointKinds.Address, ContactPointLabels.Work,
                string.Join(", ", new[] { p.City, p.Region }.Where(s => !string.IsNullOrWhiteSpace(s))), true, now);
            addr.City = CrmText.Trim(p.City);
            addr.Region = CrmText.Trim(p.Region);
            addr.Country = CrmText.Trim(p.Country);
            db.ContactPoints.Add(addr);
        }
        StampBatch(batchId);
        await db.SaveChangesAsync(ct);
        return (participant.Id, true);
    }

    private async Task EnsureLinkAsync(Guid tenantId, Guid personId, Guid orgId, string role, DateTime now, CancellationToken ct)
    {
        if (await db.Links.AnyAsync(l => l.FromParticipantId == personId && l.ToParticipantId == orgId, ct))
            return;
        var orgHasPrimary = await db.Links.AnyAsync(l => l.ToParticipantId == orgId && l.IsPrimary && l.EndedOn == null, ct);
        db.Links.Add(new CrmParticipantLinkRow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FromParticipantId = personId,
            ToParticipantId = orgId,
            Role = role,
            IsPrimary = !orgHasPrimary,
            CreatedAt = now,
        });
    }

    /// <summary>Stamps the rows an import is about to create with its batch id.</summary>
    private void StampBatch(Guid batchId)
    {
        foreach (var entry in db.ChangeTracker.Entries<CrmParticipantRow>().Where(e => e.State == EntityState.Added))
            entry.Entity.ImportBatchId = batchId;
        foreach (var entry in db.ChangeTracker.Entries<CrmContactPointRow>().Where(e => e.State == EntityState.Added))
            entry.Entity.ImportBatchId = batchId;
    }

    // ----- Reads -----

    public Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default) =>
        RunAsync("list_participants", tenantId, async () =>
        {
            if (lifecycleStage is not null && !LifecycleStages.All.Contains(lifecycleStage))
                return Invalid<IReadOnlyList<CrmParticipantSummary>>($"Unknown lifecycle stage '{lifecycleStage}'.");
            if (kind is not null && !ParticipantKinds.All.Contains(kind))
                return Invalid<IReadOnlyList<CrmParticipantSummary>>($"Unknown kind '{kind}'.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);

            var query = db.Participants.AsNoTracking().Where(p => p.TenantId == tenantId && p.ArchivedAt == null);
            if (lifecycleStage is not null) query = query.Where(p => p.LifecycleStage == lifecycleStage);
            if (kind is not null) query = query.Where(p => p.Kind == kind);

            var rows = await query
                .OrderByDescending(p => p.UpdatedAt)
                .Take(take)
                .Select(p => new CrmParticipantSummary(
                    p.Id, p.Kind, p.DisplayName, p.LifecycleStage, p.Source,
                    db.ContactPoints.Where(c => c.ParticipantId == p.Id && c.Kind == ContactPointKinds.Phone)
                        .OrderByDescending(c => c.IsPrimary).Select(c => c.Value).FirstOrDefault(),
                    db.ContactPoints.Where(c => c.ParticipantId == p.Id && c.Kind == ContactPointKinds.Email)
                        .OrderByDescending(c => c.IsPrimary).Select(c => c.Value).FirstOrDefault(),
                    p.CreatedAt,
                    db.Activities.Where(a => a.ParticipantId == p.Id).Max(a => (DateTime?)a.OccurredAt)))
                .ToListAsync(ct);
            return Result<IReadOnlyList<CrmParticipantSummary>>.Success(rows);
        }, ct);

    public Task<Result<CrmParticipant>> GetParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        RunAsync("get_participant", tenantId, async () =>
        {
            var p = await db.Participants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == participantId && x.TenantId == tenantId, ct);
            if (p is null) return NotFound<CrmParticipant>("Participant not found.");

            var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(x => x.ParticipantId == p.Id, ct);
            var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(x => x.ParticipantId == p.Id, ct);
            var contacts = await db.ContactPoints.AsNoTracking().Where(c => c.ParticipantId == p.Id)
                .OrderBy(c => c.Kind).ThenByDescending(c => c.IsPrimary).ToListAsync(ct);
            var links = await (
                from l in db.Links
                where l.TenantId == tenantId && (l.FromParticipantId == p.Id || l.ToParticipantId == p.Id)
                join other in db.Participants on (l.FromParticipantId == p.Id ? l.ToParticipantId : l.FromParticipantId) equals other.Id
                select new CrmLink(l.Id, l.FromParticipantId, l.ToParticipantId, l.Role, l.IsPrimary, other.DisplayName))
                .ToListAsync(ct);
            var activities = await db.Activities.AsNoTracking().Where(a => a.ParticipantId == p.Id)
                .OrderByDescending(a => a.OccurredAt).Take(RecentActivityCount).ToListAsync(ct);

            return Result<CrmParticipant>.Success(new CrmParticipant(
                p.Id, p.TenantId, p.Kind, p.DisplayName, p.LifecycleStage, p.Source, p.SourceDetail, p.Notes, p.CreatedAt, p.UpdatedAt,
                person is null ? null : new CrmPersonDetails(person.FirstName, person.LastName, person.PreferredName, person.JobTitle,
                    person.BirthDate, person.Gender, person.MaritalStatus, person.DependentsCount, person.EducationLevel, person.Occupation),
                org is null ? null : new CrmOrganizationDetails(org.LegalName, org.Dba, org.OrganizationType, org.NaicsCode, org.SicCode,
                    org.IndustryLabel, org.EmployeeCount, org.YearStarted, org.AnnualRevenueBand, org.Website, org.ServiceArea, org.Description),
                contacts.Select(ToContactPoint).ToList(),
                links,
                activities.Select(ToActivity).ToList()));
        }, ct);

    // ----- Deals -----

    public Task<Result<CrmDeal>> CreateDealAsync(CreateDealCommand command, CancellationToken ct = default) =>
        RunAsync("create_deal", command.TenantId, async () =>
        {
            if (string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.ProductCategory))
                return Invalid<CrmDeal>("A deal needs a name and a product category.");
            var pipeline = await db.Pipelines.AsNoTracking()
                .Where(x => x.TenantId == command.TenantId).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);
            if (pipeline is null) return NotFound<CrmDeal>("The tenant has no pipeline.");
            var stages = await db.Stages.AsNoTracking().Where(s => s.PipelineId == pipeline.Id).OrderBy(s => s.Position).ToListAsync(ct);
            var stage = command.StageName is null
                ? stages.FirstOrDefault()
                : stages.FirstOrDefault(s => string.Equals(s.Name, command.StageName, StringComparison.OrdinalIgnoreCase));
            if (stage is null) return Invalid<CrmDeal>($"Unknown stage '{command.StageName}'.");

            foreach (var id in new[] { command.OrganizationParticipantId, command.PrimaryContactParticipantId })
            {
                if (id is { } pid && !await db.Participants.AnyAsync(p => p.Id == pid && p.TenantId == command.TenantId, ct))
                    return NotFound<CrmDeal>("Unknown participant.");
            }
            if (RequiresPrimaryContact(stage, stages) && command.PrimaryContactParticipantId is null)
                return Invalid<CrmDeal>($"A deal in '{stage.Name}' needs a primary contact.");

            var now = Now();
            var deal = new CrmDealRow
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                Name = command.Name.Trim(),
                PipelineId = pipeline.Id,
                StageId = stage.Id,
                OrganizationParticipantId = command.OrganizationParticipantId,
                ProductCategory = command.ProductCategory.Trim(),
                Qualification = command.QualificationJson,
                EstimatedValue = command.EstimatedValue,
                RecurringValueMonthly = command.RecurringValueMonthly,
                ClosedAt = stage.Outcome == StageOutcomes.Open ? null : now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Deals.Add(deal);
            db.DealStageHistory.Add(new CrmDealStageHistoryRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, DealId = deal.Id, ToStageId = stage.Id, ChangedAt = now, ChangedBy = "create",
            });
            if (command.PrimaryContactParticipantId is { } contact)
                db.DealParticipants.Add(new CrmDealParticipantRow
                {
                    Id = Guid.NewGuid(), TenantId = command.TenantId, DealId = deal.Id, ParticipantId = contact, Role = DealParticipantRoles.PrimaryContact,
                });
            await db.SaveChangesAsync(ct);
            return Result<CrmDeal>.Success(ToDeal(deal, stage));
        }, ct);

    public Task<Result<CrmDeal>> MoveDealStageAsync(Guid dealId, MoveDealStageCommand command, CancellationToken ct = default) =>
        RunAsync("move_deal_stage", command.TenantId, async () =>
        {
            var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.TenantId == command.TenantId, ct);
            if (deal is null) return NotFound<CrmDeal>("Deal not found.");
            var stages = await db.Stages.AsNoTracking().Where(s => s.PipelineId == deal.PipelineId).OrderBy(s => s.Position).ToListAsync(ct);
            var target = stages.FirstOrDefault(s => string.Equals(s.Name, command.StageName, StringComparison.OrdinalIgnoreCase));
            if (target is null) return Invalid<CrmDeal>($"Unknown stage '{command.StageName}'.");
            if (target.Id == deal.StageId) return Result<CrmDeal>.Success(ToDeal(deal, target));

            if (RequiresPrimaryContact(target, stages) &&
                !await db.DealParticipants.AnyAsync(dp => dp.DealId == deal.Id && dp.Role == DealParticipantRoles.PrimaryContact, ct))
                return Invalid<CrmDeal>($"A deal needs a primary contact before it can move to '{target.Name}'.");

            var now = Now();
            db.DealStageHistory.Add(new CrmDealStageHistoryRow
            {
                Id = Guid.NewGuid(), TenantId = deal.TenantId, DealId = deal.Id, FromStageId = deal.StageId, ToStageId = target.Id,
                ChangedAt = now, ChangedBy = string.IsNullOrWhiteSpace(command.ChangedBy) ? "unknown" : command.ChangedBy,
            });
            deal.StageId = target.Id;
            deal.UpdatedAt = now;
            deal.ClosedAt = target.Outcome == StageOutcomes.Open ? null : now;
            deal.LostReason = target.Outcome == StageOutcomes.Lost ? command.LostReason : null;
            await db.SaveChangesAsync(ct);
            return Result<CrmDeal>.Success(ToDeal(deal, target));
        }, ct);

    public Task<Result<IReadOnlyList<CrmDeal>>> ListDealsAsync(Guid tenantId, bool openOnly, int limit, CancellationToken ct = default) =>
        RunAsync("list_deals", tenantId, async () =>
        {
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);
            var rows = await (
                from d in db.Deals.AsNoTracking()
                join s in db.Stages on d.StageId equals s.Id
                where d.TenantId == tenantId && (!openOnly || s.Outcome == StageOutcomes.Open)
                orderby d.UpdatedAt descending
                select new { d, s }).Take(take).ToListAsync(ct);
            return Result<IReadOnlyList<CrmDeal>>.Success(rows.Select(x => ToDeal(x.d, x.s)).ToList());
        }, ct);

    // ----- Industry lookup -----

    public Task<Result<IReadOnlyList<CrmNaicsCode>>> SearchNaicsAsync(string? query, int? level, int limit, CancellationToken ct = default) =>
        RunAsync("search_naics", null, async () =>
        {
            if (level is { } l && (l < 2 || l > 6))
                return Invalid<IReadOnlyList<CrmNaicsCode>>("level is a number from 2 to 6.");
            var text = CrmText.Trim(query);
            if (text is { Length: > 100 })
                return Invalid<IReadOnlyList<CrmNaicsCode>>("A search is at most 100 characters.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);

            var codes = db.NaicsCodes.AsNoTracking().AsQueryable();
            if (level is { } wanted) codes = codes.Where(c => c.Level == wanted);
            if (text is not null)
            {
                var like = EscapeLike(text);
                // Digits (and the dash of a sector range) search by code prefix; anything else searches titles.
                codes = text.All(ch => char.IsDigit(ch) || ch == '-')
                    ? codes.Where(c => EF.Functions.Like(c.Code, like + "%"))
                    : codes.Where(c => EF.Functions.ILike(c.Title, "%" + like + "%"));
            }
            var rows = await codes.OrderBy(c => c.Code).Take(take)
                .Select(c => new CrmNaicsCode(c.Code, c.Title, c.Level)).ToListAsync(ct);
            return Result<IReadOnlyList<CrmNaicsCode>>.Success(rows);
        }, ct);

    // ----- Custom properties -----

    public Task<Result<IReadOnlyList<CrmPropertyDefinition>>> ListPropertyDefinitionsAsync(Guid tenantId, string? objectType, bool includeRetired, bool includeSensitive, CancellationToken ct = default) =>
        RunAsync("list_property_definitions", tenantId, async () =>
        {
            if (objectType is not null && !PropertyObjectTypes.All.Contains(objectType))
                return Invalid<IReadOnlyList<CrmPropertyDefinition>>($"Unknown object type '{objectType}'.");
            var definitions = db.PropertyDefinitions.AsNoTracking().Where(d => d.TenantId == tenantId);
            if (objectType is not null) definitions = definitions.Where(d => d.ObjectType == objectType);
            if (!includeRetired) definitions = definitions.Where(d => d.RetiredAt == null);
            if (!includeSensitive) definitions = definitions.Where(d => !d.IsSensitive);
            var rows = await definitions.OrderBy(d => d.ObjectType).ThenBy(d => d.Key).ToListAsync(ct);
            return Result<IReadOnlyList<CrmPropertyDefinition>>.Success(rows.Select(ToDefinition).ToList());
        }, ct);

    public Task<Result<CrmPropertyDefinition>> CreatePropertyDefinitionAsync(CreatePropertyDefinitionCommand command, CancellationToken ct = default) =>
        RunAsync("create_property_definition", command.TenantId, async () =>
        {
            if (CrmPropertyRules.ValidateDefinition(command.ObjectType, command.Key, command.Label, command.DataType, command.Options) is { } problem)
                return Invalid<CrmPropertyDefinition>(problem);
            if (!await TenantExistsAsync(command.TenantId, ct))
                return NotFound<CrmPropertyDefinition>("Unknown tenant.");

            var row = new CrmPropertyDefinitionRow
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                ObjectType = command.ObjectType,
                Key = command.Key,
                Label = command.Label.Trim(),
                DataType = command.DataType,
                Options = CrmPropertyRules.UsesOptions(command.DataType) ? JsonSerializer.Serialize(command.Options!.Select(o => o.Trim())) : null,
                IsSensitive = command.IsSensitive,
                CreatedAt = Now(),
            };
            db.PropertyDefinitions.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                return Result<CrmPropertyDefinition>.Failure(
                    $"A {command.ObjectType} property with the key '{command.Key}' already exists (a retired one keeps its key).", CrmFailureCodes.Conflict);
            }
            return Result<CrmPropertyDefinition>.Success(ToDefinition(row));
        }, ct);

    public Task<Result<CrmPropertyDefinition>> UpdatePropertyDefinitionAsync(Guid definitionId, UpdatePropertyDefinitionCommand command, CancellationToken ct = default) =>
        RunAsync("update_property_definition", command.TenantId, async () =>
        {
            var row = await db.PropertyDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId && d.TenantId == command.TenantId, ct);
            if (row is null) return NotFound<CrmPropertyDefinition>("Property not found.");
            if (row.RetiredAt is not null) return Invalid<CrmPropertyDefinition>("A retired property cannot be edited.");
            if (CrmPropertyRules.ValidateDefinition(row.ObjectType, row.Key, command.Label, row.DataType, command.Options, checkIdentity: false) is { } problem)
                return Invalid<CrmPropertyDefinition>(problem);

            if (CrmPropertyRules.UsesOptions(row.DataType))
            {
                var kept = command.Options!.Select(o => o.Trim()).ToList();
                var stored = await db.PropertyValues.AsNoTracking().Where(v => v.DefinitionId == row.Id).Select(v => v.Value).ToListAsync(ct);
                var orphaned = stored.Count(json =>
                {
                    using var doc = JsonDocument.Parse(json);
                    return CrmPropertyRules.ValidateValue(row.DataType, kept, doc.RootElement) is not null;
                });
                if (orphaned > 0)
                    return Invalid<CrmPropertyDefinition>($"{orphaned} stored value(s) use an option that would be removed. Change those values first.");
                row.Options = JsonSerializer.Serialize(kept);
            }
            row.Label = command.Label.Trim();
            row.IsSensitive = command.IsSensitive;
            await db.SaveChangesAsync(ct);
            return Result<CrmPropertyDefinition>.Success(ToDefinition(row));
        }, ct);

    public Task<Result<CrmPropertyDefinition>> RetirePropertyDefinitionAsync(Guid tenantId, Guid definitionId, CancellationToken ct = default) =>
        RunAsync("retire_property_definition", tenantId, async () =>
        {
            var row = await db.PropertyDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId && d.TenantId == tenantId, ct);
            if (row is null) return NotFound<CrmPropertyDefinition>("Property not found.");
            if (row.RetiredAt is null)
            {
                row.RetiredAt = Now();
                await db.SaveChangesAsync(ct);
            }
            return Result<CrmPropertyDefinition>.Success(ToDefinition(row));
        }, ct);

    public Task<Result<IReadOnlyList<CrmPropertyValue>>> GetPropertyValuesAsync(Guid tenantId, string objectType, Guid recordId, bool includeSensitive, CancellationToken ct = default) =>
        RunAsync("get_property_values", tenantId, async () =>
        {
            if (!PropertyObjectTypes.All.Contains(objectType))
                return Invalid<IReadOnlyList<CrmPropertyValue>>($"Unknown object type '{objectType}'.");
            if (!await RecordExistsAsync(tenantId, objectType, recordId, ct))
                return NotFound<IReadOnlyList<CrmPropertyValue>>("Record not found.");
            return Result<IReadOnlyList<CrmPropertyValue>>.Success(await ReadPropertyValuesAsync(tenantId, objectType, recordId, includeSensitive, ct));
        }, ct);

    public Task<Result<IReadOnlyList<CrmPropertyValue>>> SetPropertyValuesAsync(SetPropertyValuesCommand command, CancellationToken ct = default) =>
        RunAsync("set_property_values", command.TenantId, async () =>
        {
            const int MaxValuesPerCall = 100;
            if (!PropertyObjectTypes.All.Contains(command.ObjectType))
                return Invalid<IReadOnlyList<CrmPropertyValue>>($"Unknown object type '{command.ObjectType}'.");
            if (command.Values is null || command.Values.Count == 0)
                return Invalid<IReadOnlyList<CrmPropertyValue>>("No values to set.");
            if (command.Values.Count > MaxValuesPerCall)
                return Invalid<IReadOnlyList<CrmPropertyValue>>($"At most {MaxValuesPerCall} values per call.");
            if (!await RecordExistsAsync(command.TenantId, command.ObjectType, command.RecordId, ct))
                return NotFound<IReadOnlyList<CrmPropertyValue>>("Record not found.");

            // A property that does not exist, is retired, or is sensitive for a caller who may not see it is
            // answered the same way, so the answer never says which.
            var keys = command.Values.Keys.ToList();
            var definitions = await db.PropertyDefinitions
                .Where(d => d.TenantId == command.TenantId && d.ObjectType == command.ObjectType && d.RetiredAt == null && keys.Contains(d.Key))
                .ToListAsync(ct);
            var byKey = definitions.Where(d => command.IncludeSensitive || !d.IsSensitive).ToDictionary(d => d.Key, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!byKey.ContainsKey(key))
                    return NotFound<IReadOnlyList<CrmPropertyValue>>($"Unknown property '{key}'.");
            }

            // Every value is checked before any is written.
            foreach (var (key, value) in command.Values)
            {
                if (value.ValueKind == JsonValueKind.Null) continue;
                var definition = byKey[key];
                if (CrmPropertyRules.ValidateValue(definition.DataType, ParseOptions(definition.Options), value) is { } problem)
                    return Invalid<IReadOnlyList<CrmPropertyValue>>($"{key}: {problem}");
            }

            var definitionIds = byKey.Values.Select(d => d.Id).ToList();
            var existing = await db.PropertyValues
                .Where(v => v.RecordId == command.RecordId && definitionIds.Contains(v.DefinitionId))
                .ToDictionaryAsync(v => v.DefinitionId, ct);
            var now = Now();
            foreach (var (key, value) in command.Values)
            {
                var definition = byKey[key];
                existing.TryGetValue(definition.Id, out var current);
                if (value.ValueKind == JsonValueKind.Null)
                {
                    if (current is not null) db.PropertyValues.Remove(current);
                }
                else if (current is not null)
                {
                    current.Value = value.GetRawText();
                    current.UpdatedAt = now;
                }
                else
                {
                    db.PropertyValues.Add(new CrmPropertyValueRow
                    {
                        Id = Guid.NewGuid(),
                        TenantId = command.TenantId,
                        DefinitionId = definition.Id,
                        RecordId = command.RecordId,
                        Value = value.GetRawText(),
                        UpdatedAt = now,
                    });
                }
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                return Result<IReadOnlyList<CrmPropertyValue>>.Failure("The record's properties changed while saving; read them again and retry.", CrmFailureCodes.Conflict);
            }
            return Result<IReadOnlyList<CrmPropertyValue>>.Success(
                await ReadPropertyValuesAsync(command.TenantId, command.ObjectType, command.RecordId, command.IncludeSensitive, ct));
        }, ct);

    private async Task<IReadOnlyList<CrmPropertyValue>> ReadPropertyValuesAsync(Guid tenantId, string objectType, Guid recordId, bool includeSensitive, CancellationToken ct)
    {
        var rows = await (
            from v in db.PropertyValues.AsNoTracking()
            join d in db.PropertyDefinitions.AsNoTracking() on v.DefinitionId equals d.Id
            where v.RecordId == recordId && d.TenantId == tenantId && d.ObjectType == objectType
                  && d.RetiredAt == null && (includeSensitive || !d.IsSensitive)
            orderby d.Key
            select new { d.Id, d.Key, d.Label, d.DataType, d.IsSensitive, v.Value }).ToListAsync(ct);
        return rows.Select(r =>
        {
            using var doc = JsonDocument.Parse(r.Value);
            return new CrmPropertyValue(r.Id, r.Key, r.Label, r.DataType, r.IsSensitive, doc.RootElement.Clone());
        }).ToList();
    }

    private async Task<bool> RecordExistsAsync(Guid tenantId, string objectType, Guid recordId, CancellationToken ct) =>
        objectType == PropertyObjectTypes.Deal
            ? await db.Deals.AnyAsync(d => d.Id == recordId && d.TenantId == tenantId, ct)
            : await db.Participants.AnyAsync(p => p.Id == recordId && p.TenantId == tenantId && p.Kind == objectType, ct);

    private static IReadOnlyList<string>? ParseOptions(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<List<string>>(json);

    private static CrmPropertyDefinition ToDefinition(CrmPropertyDefinitionRow d) =>
        new(d.Id, d.ObjectType, d.Key, d.Label, d.DataType, ParseOptions(d.Options), d.IsSensitive, d.RetiredAt, d.CreatedAt);

    // ----- Helpers -----

    /// <summary>Past "Qualified" (by position) a deal needs a primary contact person. Lost never does.</summary>
    public static bool RequiresPrimaryContact(CrmStageRow target, IReadOnlyList<CrmStageRow> stages)
    {
        if (target.Outcome == StageOutcomes.Lost) return false;
        var qualified = stages.FirstOrDefault(s => string.Equals(s.Name, "Qualified", StringComparison.OrdinalIgnoreCase));
        return qualified is not null && target.Position > qualified.Position;
    }

    /// <summary>Escapes LIKE wildcards so a company name containing % or _ matches only itself.</summary>
    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct) => db.Tenants.AnyAsync(t => t.Id == tenantId, ct);

    private async Task<Guid?> FindActivityAsync(RecordActivityCommand command, CancellationToken ct) =>
        await db.Activities.AsNoTracking()
            .Where(a => a.TenantId == command.TenantId && a.ExternalRef == command.ExternalRef
                        && a.Channel == command.Channel && a.Outcome == command.Outcome)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static CrmParticipantRow NewParticipant(Guid tenantId, string kind, string displayName, string source, string? sourceDetail, string? externalId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Kind = kind,
        DisplayName = displayName,
        LifecycleStage = LifecycleStages.Lead,
        Source = source,
        SourceDetail = sourceDetail,
        ExternalId = externalId,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static CrmContactPointRow NewContactPoint(Guid tenantId, Guid participantId, string kind, string label, string value, bool isPrimary, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ParticipantId = participantId,
        Kind = kind,
        Label = label,
        Value = value,
        IsPrimary = isPrimary,
        CreatedAt = now,
    };

    private static CrmContactPoint ToContactPoint(CrmContactPointRow c) => new(
        c.Id, c.Kind, c.Label, c.Value, c.IsPrimary, c.DoNotContact, c.SmsOptOut,
        c.AddressLine1, c.AddressLine2, c.City, c.Region, c.PostalCode, c.Country);

    private static CrmActivity ToActivity(CrmActivityRow a) => new(
        a.Id, a.ParticipantId, a.DealId, a.Channel, a.Direction, a.OccurredAt, a.Subject, a.Body, a.Summary,
        a.RecordingUrl, a.DurationSeconds, a.Outcome, a.ExternalRef, a.CreatedBy, a.Priority, a.StartsAt, a.EndsAt, a.CalendarEventId);

    private static CrmDeal ToDeal(CrmDealRow d, CrmStageRow s) => new(
        d.Id, d.Name, d.PipelineId, d.StageId, s.Name, s.Outcome, d.OrganizationParticipantId, d.ProductCategory,
        d.Qualification, d.EstimatedValue, d.RecurringValueMonthly, d.ExpectedCloseOn, d.ClosedAt, d.LostReason, d.CreatedAt, d.EstimateGivenAt);

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.InvalidRequest);
    private static Result<T> NotFound<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.NotFound);

    /// <summary>
    /// Runs one operation for a tenant inside its transaction (a null tenant, for platform data such as the NAICS list,
    /// runs without one). The transaction commits when the result is a success that <paramref name="discard"/> does not
    /// discard, and rolls back otherwise, so a refused operation leaves nothing behind.
    /// </summary>
    private async Task<Result<T>> RunAsync<T>(string operation, Guid? tenantId, Func<Task<Result<T>>> work, CancellationToken ct, Func<T, bool>? discard = null, bool platform = false)
    {
        try
        {
            if (tenantId is not { } tenant) return await work();
            Func<Result<T>, bool> keep = result => result.IsSuccess && !(discard?.Invoke(result.Value!) ?? false);
            return await (platform ? scope.RunPlatformAsync(tenant, work, keep, ct) : scope.RunAsync(tenant, work, keep, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "geek_crm {Operation} failed", operation);
            return Result<T>.Failure($"The CRM store could not complete {operation}.", CrmFailureCodes.StoreUnavailable);
        }
    }
}
