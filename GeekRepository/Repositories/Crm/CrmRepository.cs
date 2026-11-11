using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.Crm;

/// <summary>
/// geek_crm in GeekRepository's own Postgres (docs/GEEK-CRM-DATA-MODEL.md). Every query is scoped by
/// tenant. Every method answers a Result and nothing throws past it: a database failure is
/// store_unavailable, logged by operation and never with a person's details.
/// </summary>
public sealed class CrmRepository(CrmDbContext db, TimeProvider clock, ILogger<CrmRepository> logger) : ICrmStore
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 200;
    private const int RecentActivityCount = 25;

    // ----- Receptionist -----

    public Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default) =>
        RunAsync("ensure_participant_by_phone", async () =>
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
        RunAsync("record_activity", async () =>
        {
            if (!ActivityChannels.All.Contains(command.Channel))
                return Invalid<RecordActivityResult>($"Unknown channel '{command.Channel}'.");
            if (command.Direction is not null && !ActivityDirections.All.Contains(command.Direction))
                return Invalid<RecordActivityResult>($"Unknown direction '{command.Direction}'.");
            if (command.Outcome is not null && !ActivityOutcomes.All.Contains(command.Outcome))
                return Invalid<RecordActivityResult>($"Unknown outcome '{command.Outcome}'.");
            if (string.IsNullOrWhiteSpace(command.CreatedBy))
                return Invalid<RecordActivityResult>("createdBy is required.");
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
            };
            db.Activities.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A retry raced this one in: the other insert is the activity.
                db.ChangeTracker.Clear();
                if (await FindActivityAsync(command, ct) is { } raced)
                    return Result<RecordActivityResult>.Success(new RecordActivityResult(raced, false));
                throw;
            }

            if (command.ParticipantId is { } touched)
                await db.Participants.Where(p => p.Id == touched)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, row.CreatedAt), ct);

            return Result<RecordActivityResult>.Success(new RecordActivityResult(row.Id, true));
        }, ct);

    public Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default) =>
        RunAsync("set_sms_opt_out", async () =>
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
        RunAsync("import_prospects", async () =>
        {
            if (command.Prospects is null || command.Prospects.Count == 0)
                return Invalid<ImportProspectsResult>("No prospects to import.");
            if (command.Prospects.Count > 5000)
                return Invalid<ImportProspectsResult>("At most 5,000 prospects per import.");
            if (!await TenantExistsAsync(command.TenantId, ct))
                return NotFound<ImportProspectsResult>("Unknown tenant.");

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
                    var (id, created) = await EnsureOrganizationAsync(command, p, now, ct);
                    orgId = id;
                    if (created) orgs++; else matched++;
                }

                if (hasPerson)
                {
                    var (personId, created) = await EnsurePersonAsync(command, p, now, ct);
                    if (created) people++; else matched++;
                    if (orgId is { } o)
                        await EnsureLinkAsync(command.TenantId, personId, o, CrmText.RoleFromTitle(p.PersonRoleAtCompany ?? p.JobTitle), now, ct);
                }

                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            return Result<ImportProspectsResult>.Success(new ImportProspectsResult(people, orgs, matched, skipped, problems));
        }, ct);

    private async Task<(Guid Id, bool Created)> EnsureOrganizationAsync(ImportProspectsCommand command, ImportProspect p, DateTime now, CancellationToken ct)
    {
        var name = p.CompanyName!.Trim();
        var domain = CrmText.Domain(p.CompanyWebsite);

        var existing = await (
            from part in db.Participants
            join org in db.Organizations on part.Id equals org.ParticipantId
            where part.TenantId == command.TenantId && part.ArchivedAt == null
                  && (EF.Functions.ILike(org.LegalName, CrmRepository.EscapeLike(name))
                      || (domain != null && org.Website != null && EF.Functions.ILike(org.Website, "%" + CrmRepository.EscapeLike(domain) + "%")))
            select part.Id).FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty) return (existing, false);

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
        await db.SaveChangesAsync(ct);
        return (participant.Id, true);
    }

    private async Task<(Guid Id, bool Created)> EnsurePersonAsync(ImportProspectsCommand command, ImportProspect p, DateTime now, CancellationToken ct)
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

    // ----- Reads -----

    public Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default) =>
        RunAsync("list_participants", async () =>
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
        RunAsync("get_participant", async () =>
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
        RunAsync("create_deal", async () =>
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
        RunAsync("move_deal_stage", async () =>
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
        RunAsync("list_deals", async () =>
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
        a.RecordingUrl, a.DurationSeconds, a.Outcome, a.ExternalRef, a.CreatedBy);

    private static CrmDeal ToDeal(CrmDealRow d, CrmStageRow s) => new(
        d.Id, d.Name, d.PipelineId, d.StageId, s.Name, s.Outcome, d.OrganizationParticipantId, d.ProductCategory,
        d.Qualification, d.EstimatedValue, d.RecurringValueMonthly, d.ExpectedCloseOn, d.ClosedAt, d.LostReason, d.CreatedAt);

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.InvalidRequest);
    private static Result<T> NotFound<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.NotFound);

    private async Task<Result<T>> RunAsync<T>(string operation, Func<Task<Result<T>>> work, CancellationToken ct)
    {
        try
        {
            return await work();
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
