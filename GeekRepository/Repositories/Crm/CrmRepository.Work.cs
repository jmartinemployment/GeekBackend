using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Crm;

// The work a person does at the keyboard (ICrmWorkStore): find and edit people and companies, work deals,
// keep notes and tasks, and the numbers for the home screen.
public sealed partial class CrmRepository : ICrmWorkStore
{
    private const int MaxNotesLength = 20_000;
    private const int MaxTextLength = 300;
    private const int UrgentWindowDays = 7;

    // ----- Pipeline and home screen -----

    public Task<Result<IReadOnlyList<CrmStage>>> ListStagesAsync(Guid tenantId, CancellationToken ct = default) =>
        RunAsync("list_stages", tenantId, async () =>
            Result<IReadOnlyList<CrmStage>>.Success(await DefaultStagesAsync(tenantId, ct)), ct);

    public Task<Result<CrmDashboard>> GetDashboardAsync(Guid tenantId, CancellationToken ct = default) =>
        RunAsync("get_dashboard", tenantId, async () =>
        {
            var now = Now();
            var stages = await DefaultStagesAsync(tenantId, ct);
            var totals = await db.Deals.AsNoTracking()
                .Where(d => d.TenantId == tenantId)
                .GroupBy(d => d.StageId)
                .Select(g => new
                {
                    StageId = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(d => (decimal?)d.EstimatedValue) ?? 0m,
                    Recurring = g.Sum(d => (decimal?)d.RecurringValueMonthly) ?? 0m,
                })
                .ToListAsync(ct);
            var stageTotals = stages
                .Select(s =>
                {
                    var t = totals.FirstOrDefault(x => x.StageId == s.Id);
                    return new CrmStageTotal(s.Id, s.Name, s.Position, s.Outcome, t?.Count ?? 0, t?.Value ?? 0m, t?.Recurring ?? 0m);
                })
                .ToList();

            var live = db.Participants.AsNoTracking().Where(p => p.TenantId == tenantId && p.ArchivedAt == null);
            var people = await live.CountAsync(p => p.Kind == ParticipantKinds.Person, ct);
            var organizations = await live.CountAsync(p => p.Kind == ParticipantKinds.Organization, ct);

            var openTasks = db.Activities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Channel == ActivityChannels.Task && a.CompletedAt == null);
            var open = await openTasks.CountAsync(ct);
            var overdue = await openTasks.CountAsync(a => a.DueAt != null && a.DueAt < now, ct);

            var since = now.AddDays(-UrgentWindowDays);
            var urgent = await db.Activities.AsNoTracking()
                .CountAsync(a => a.TenantId == tenantId && a.Priority == ActivityPriorities.Urgent && a.OccurredAt >= since, ct);

            return Result<CrmDashboard>.Success(new CrmDashboard(stageTotals, people, organizations, open, overdue, urgent));
        }, ct);

    private async Task<IReadOnlyList<CrmStage>> DefaultStagesAsync(Guid tenantId, CancellationToken ct)
    {
        var pipelineId = await db.Pipelines.AsNoTracking()
            .Where(x => x.TenantId == tenantId).OrderByDescending(x => x.IsDefault)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (pipelineId is not { } id) return [];
        return await db.Stages.AsNoTracking()
            .Where(s => s.PipelineId == id).OrderBy(s => s.Position)
            .Select(s => new CrmStage(s.Id, s.Name, s.Position, s.Outcome, s.ProbabilityPct))
            .ToListAsync(ct);
    }

    // ----- People and companies -----

    public Task<Result<IReadOnlyList<CrmParticipantSummary>>> SearchParticipantsAsync(Guid tenantId, string? search, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default) =>
        RunAsync("search_participants", tenantId, async () =>
        {
            if (lifecycleStage is not null && !LifecycleStages.All.Contains(lifecycleStage))
                return Invalid<IReadOnlyList<CrmParticipantSummary>>($"Unknown lifecycle stage '{lifecycleStage}'.");
            if (kind is not null && !ParticipantKinds.All.Contains(kind))
                return Invalid<IReadOnlyList<CrmParticipantSummary>>($"Unknown kind '{kind}'.");
            var text = CrmText.Trim(search);
            if (text is { Length: > 100 })
                return Invalid<IReadOnlyList<CrmParticipantSummary>>("A search is at most 100 characters.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);

            var query = db.Participants.AsNoTracking().Where(p => p.TenantId == tenantId && p.ArchivedAt == null);
            if (lifecycleStage is not null) query = query.Where(p => p.LifecycleStage == lifecycleStage);
            if (kind is not null) query = query.Where(p => p.Kind == kind);
            if (text is not null)
            {
                var like = "%" + EscapeLike(text) + "%";
                // Typing digits finds a phone number however it was written: numbers are stored as +15615550100.
                var digits = new string(text.Where(char.IsDigit).ToArray());
                var phoneLike = digits.Length >= 3 && !text.Any(char.IsLetter) ? "%" + digits + "%" : null;
                query = query.Where(p =>
                    EF.Functions.ILike(p.DisplayName, like)
                    || db.ContactPoints.Any(c => c.ParticipantId == p.Id
                        && ((c.Kind == ContactPointKinds.Email && EF.Functions.ILike(c.Value, like))
                            || (phoneLike != null && c.Kind == ContactPointKinds.Phone && EF.Functions.ILike(c.Value, phoneLike)))));
            }

            var rows = await SummariesOf(query.OrderByDescending(p => p.UpdatedAt).Take(take)).ToListAsync(ct);
            return Result<IReadOnlyList<CrmParticipantSummary>>.Success(rows);
        }, ct);

    public Task<Result<CrmParticipant>> CreateParticipantAsync(CreateParticipantCommand command, CancellationToken ct = default) =>
        RunAsync("create_participant", command.TenantId, async () =>
        {
            if (!ParticipantKinds.All.Contains(command.Kind)) return Invalid<CrmParticipant>($"Unknown kind '{command.Kind}'.");
            if (!LifecycleStages.All.Contains(command.LifecycleStage)) return Invalid<CrmParticipant>($"Unknown lifecycle stage '{command.LifecycleStage}'.");
            if (!ParticipantSources.All.Contains(command.Source)) return Invalid<CrmParticipant>($"Unknown source '{command.Source}'.");
            if (!await TenantExistsAsync(command.TenantId, ct)) return NotFound<CrmParticipant>("Unknown tenant.");

            var contact = ReadContactInput(command.Email, command.PhoneE164);
            if (contact.Problem is not null) return Invalid<CrmParticipant>(contact.Problem);
            var notes = CrmText.Trim(command.Notes);
            if (notes is { Length: > MaxNotesLength }) return Invalid<CrmParticipant>($"Notes are at most {MaxNotesLength:N0} characters.");
            var now = Now();

            if (command.Kind == ParticipantKinds.Person)
            {
                var first = CrmText.Trim(command.FirstName);
                var last = CrmText.Trim(command.LastName);
                var title = CrmText.Trim(command.JobTitle);
                if (first is null && last is null) return Invalid<CrmParticipant>("A person needs a first or last name.");
                if (TooLong(first, last, title)) return Invalid<CrmParticipant>($"Names and titles are at most {MaxTextLength} characters.");
                if (contact.Email is not null && await EmailTakenAsync(command.TenantId, contact.Email, null, ct) is { } owner)
                    return Conflict<CrmParticipant>($"{owner} already has the email address {contact.Email}.");

                var (linkedOrgId, linkRole, linkProblem) = await ResolveLinkAsync(command, title, ct);
                if (linkProblem is not null) return linkProblem;

                var participant = NewParticipant(command.TenantId, ParticipantKinds.Person, DisplayOf(first, last), command.Source, null, null, now);
                participant.LifecycleStage = command.LifecycleStage;
                participant.Notes = notes;
                db.Participants.Add(participant);
                db.Persons.Add(new CrmPersonRow { ParticipantId = participant.Id, FirstName = first, LastName = last, JobTitle = title });
                AddContacts(command.TenantId, participant.Id, contact, ContactPointLabels.Work, now);
                if (linkedOrgId is { } orgId)
                    await EnsureLinkAsync(command.TenantId, participant.Id, orgId, linkRole!, now, ct);
                await db.SaveChangesAsync(ct);
                return await LoadParticipantAsync(command.TenantId, participant.Id, ct);
            }

            var name = CrmText.Trim(command.OrganizationName);
            var website = CrmText.Trim(command.Website);
            if (name is null) return Invalid<CrmParticipant>("A company needs a name.");
            if (TooLong(name, website)) return Invalid<CrmParticipant>($"Names and websites are at most {MaxTextLength} characters.");
            if (command.LinkedOrganizationId is not null) return Invalid<CrmParticipant>("A company cannot be linked to another company here.");
            if (await OrganizationTakenAsync(command.TenantId, name, website, null, ct) is { } existing)
                return Conflict<CrmParticipant>($"{existing} is already in the CRM with that name or website.");

            var org = NewParticipant(command.TenantId, ParticipantKinds.Organization, name, command.Source, null, null, now);
            org.LifecycleStage = command.LifecycleStage;
            org.Notes = notes;
            db.Participants.Add(org);
            db.Organizations.Add(new CrmOrganizationRow { ParticipantId = org.Id, LegalName = name, Website = website });
            AddContacts(command.TenantId, org.Id, contact, ContactPointLabels.Main, now);
            await db.SaveChangesAsync(ct);
            return await LoadParticipantAsync(command.TenantId, org.Id, ct);
        }, ct);

    public Task<Result<CrmParticipant>> UpdateParticipantAsync(Guid participantId, UpdateParticipantCommand command, CancellationToken ct = default) =>
        RunAsync("update_participant", command.TenantId, async () =>
        {
            if (!LifecycleStages.All.Contains(command.LifecycleStage)) return Invalid<CrmParticipant>($"Unknown lifecycle stage '{command.LifecycleStage}'.");
            var p = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId && x.TenantId == command.TenantId, ct);
            if (p is null) return NotFound<CrmParticipant>("Participant not found.");
            if (p.ArchivedAt is not null) return Invalid<CrmParticipant>("An archived record cannot be edited.");

            var contact = ReadContactInput(command.Email, command.PhoneE164);
            if (contact.Problem is not null) return Invalid<CrmParticipant>(contact.Problem);
            var notes = CrmText.Trim(command.Notes);
            if (notes is { Length: > MaxNotesLength }) return Invalid<CrmParticipant>($"Notes are at most {MaxNotesLength:N0} characters.");
            var now = Now();

            if (p.Kind == ParticipantKinds.Person)
            {
                var first = CrmText.Trim(command.FirstName);
                var last = CrmText.Trim(command.LastName);
                var title = CrmText.Trim(command.JobTitle);
                if (first is null && last is null) return Invalid<CrmParticipant>("A person needs a first or last name.");
                if (TooLong(first, last, title)) return Invalid<CrmParticipant>($"Names and titles are at most {MaxTextLength} characters.");
                if (contact.Email is not null && await EmailTakenAsync(command.TenantId, contact.Email, p.Id, ct) is { } owner)
                    return Conflict<CrmParticipant>($"{owner} already has the email address {contact.Email}.");

                var person = await db.Persons.FirstOrDefaultAsync(x => x.ParticipantId == p.Id, ct);
                if (person is null) return NotFound<CrmParticipant>("Person details not found.");
                person.FirstName = first;
                person.LastName = last;
                person.JobTitle = title;
                p.DisplayName = DisplayOf(first, last);
            }
            else
            {
                var name = CrmText.Trim(command.OrganizationName);
                var website = CrmText.Trim(command.Website);
                if (name is null) return Invalid<CrmParticipant>("A company needs a name.");
                if (TooLong(name, website)) return Invalid<CrmParticipant>($"Names and websites are at most {MaxTextLength} characters.");
                if (await OrganizationTakenAsync(command.TenantId, name, website, p.Id, ct) is { } existing)
                    return Conflict<CrmParticipant>($"{existing} is already in the CRM with that name or website.");

                var org = await db.Organizations.FirstOrDefaultAsync(x => x.ParticipantId == p.Id, ct);
                if (org is null) return NotFound<CrmParticipant>("Company details not found.");
                org.LegalName = name;
                org.Website = website;
                p.DisplayName = name;
            }

            p.LifecycleStage = command.LifecycleStage;
            p.Notes = notes;
            p.UpdatedAt = now;
            if (contact.Email is not null) await SetPrimaryContactAsync(p, ContactPointKinds.Email, ContactPointLabels.Work, contact.Email, now, ct);
            if (contact.Phone is not null) await SetPrimaryContactAsync(p, ContactPointKinds.Phone, p.Kind == ParticipantKinds.Person ? ContactPointLabels.Mobile : ContactPointLabels.Main, contact.Phone, now, ct);
            await db.SaveChangesAsync(ct);
            return await LoadParticipantAsync(command.TenantId, p.Id, ct);
        }, ct);

    public Task<Result<bool>> ArchiveParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        RunAsync("archive_participant", tenantId, async () =>
        {
            var p = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId && x.TenantId == tenantId, ct);
            if (p is null) return NotFound<bool>("Participant not found.");
            if (p.ArchivedAt is null)
            {
                p.ArchivedAt = Now();
                p.UpdatedAt = p.ArchivedAt.Value;
                await db.SaveChangesAsync(ct);
            }
            return Result<bool>.Success(true);
        }, ct);

    private static bool TooLong(params string?[] values) => values.Any(v => v is { Length: > MaxTextLength });

    private static string DisplayOf(string? first, string? last) => string.Join(' ', new[] { first, last }.Where(s => s is not null));

    private sealed record ContactInput(string? Email, string? Phone, string? Problem);

    /// <summary>Reads what a form typed: a blank is "nothing entered", and anything entered that cannot be an email or a phone is refused.</summary>
    private static ContactInput ReadContactInput(string? rawEmail, string? rawPhone)
    {
        var email = CrmText.Email(rawEmail);
        if (!string.IsNullOrWhiteSpace(rawEmail) && email is null) return new ContactInput(null, null, "That email address is not valid.");
        var phone = CrmText.ToE164(rawPhone);
        if (!string.IsNullOrWhiteSpace(rawPhone) && phone is null)
            return new ContactInput(null, null, "That phone number is not valid. Use ten digits, or start with + and the country code.");
        return new ContactInput(email, phone, null);
    }

    private void AddContacts(Guid tenantId, Guid participantId, ContactInput contact, string label, DateTime now)
    {
        if (contact.Email is not null)
            db.ContactPoints.Add(NewContactPoint(tenantId, participantId, ContactPointKinds.Email, ContactPointLabels.Work, contact.Email, true, now));
        if (contact.Phone is not null)
            db.ContactPoints.Add(NewContactPoint(tenantId, participantId, ContactPointKinds.Phone, label, contact.Phone, true, now));
    }

    private async Task SetPrimaryContactAsync(CrmParticipantRow p, string kind, string label, string value, DateTime now, CancellationToken ct)
    {
        var current = await db.ContactPoints.FirstOrDefaultAsync(c => c.ParticipantId == p.Id && c.Kind == kind && c.IsPrimary, ct);
        if (current is null) db.ContactPoints.Add(NewContactPoint(p.TenantId, p.Id, kind, label, value, true, now));
        else current.Value = value;
    }

    /// <summary>The display name of the person who already has this email, or null when nobody does (the person being edited does not count).</summary>
    private Task<string?> EmailTakenAsync(Guid tenantId, string email, Guid? except, CancellationToken ct) =>
        (from c in db.ContactPoints.AsNoTracking()
         join p in db.Participants.AsNoTracking() on c.ParticipantId equals p.Id
         where c.TenantId == tenantId && c.Kind == ContactPointKinds.Email && c.Value == email
               && p.ArchivedAt == null && (except == null || p.Id != except)
         select p.DisplayName).FirstOrDefaultAsync(ct);

    /// <summary>The display name of the company with this name or this website's domain, or null when there is none (the company being edited does not count).</summary>
    private async Task<string?> OrganizationTakenAsync(Guid tenantId, string name, string? website, Guid? except, CancellationToken ct)
    {
        var domain = CrmText.Domain(website);
        var candidates = await (
            from part in db.Participants.AsNoTracking()
            join org in db.Organizations.AsNoTracking() on part.Id equals org.ParticipantId
            where part.TenantId == tenantId && part.ArchivedAt == null && (except == null || part.Id != except)
                  && (EF.Functions.ILike(org.LegalName, EscapeLike(name))
                      || (domain != null && org.Website != null && EF.Functions.ILike(org.Website, "%" + EscapeLike(domain) + "%")))
            select new { part.DisplayName, org.LegalName, org.Website }).ToListAsync(ct);
        return candidates.FirstOrDefault(c =>
            string.Equals(c.LegalName, name, StringComparison.OrdinalIgnoreCase) || CrmText.SameDomain(c.Website, website))?.DisplayName;
    }

    /// <summary>Checks the company a new person is being linked to: it exists in this tenant, is a company, and the role is a known one.</summary>
    private async Task<(Guid? OrganizationId, string? Role, Result<CrmParticipant>? Problem)> ResolveLinkAsync(CreateParticipantCommand command, string? title, CancellationToken ct)
    {
        if (command.LinkedOrganizationId is not { } orgId)
            return command.LinkRole is null ? (null, null, null) : (null, null, Invalid<CrmParticipant>("A role needs a company to link to."));
        var role = command.LinkRole ?? CrmText.RoleFromTitle(title);
        if (!ParticipantLinkRoles.All.Contains(role)) return (null, null, Invalid<CrmParticipant>($"Unknown role '{role}'."));
        var isOrganization = await db.Participants.AnyAsync(x => x.Id == orgId && x.TenantId == command.TenantId && x.Kind == ParticipantKinds.Organization, ct);
        return isOrganization ? (orgId, role, null) : (null, null, NotFound<CrmParticipant>("That company was not found."));
    }

    // ----- Deals -----

    public Task<Result<IReadOnlyList<CrmDealView>>> ListDealViewsAsync(Guid tenantId, bool openOnly, string? search, int limit, CancellationToken ct = default) =>
        RunAsync("list_deal_views", tenantId, async () =>
        {
            var text = CrmText.Trim(search);
            if (text is { Length: > 100 }) return Invalid<IReadOnlyList<CrmDealView>>("A search is at most 100 characters.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);

            var query =
                from d in db.Deals.AsNoTracking()
                join s in db.Stages.AsNoTracking() on d.StageId equals s.Id
                where d.TenantId == tenantId && (!openOnly || s.Outcome == StageOutcomes.Open)
                select new { d, s };
            if (text is not null)
            {
                var like = "%" + EscapeLike(text) + "%";
                query = query.Where(x => EF.Functions.ILike(x.d.Name, like)
                    || (x.d.OrganizationParticipantId != null
                        && db.Participants.Any(p => p.Id == x.d.OrganizationParticipantId && EF.Functions.ILike(p.DisplayName, like))));
            }
            var rows = await query.OrderByDescending(x => x.d.UpdatedAt).Take(take).ToListAsync(ct);
            return Result<IReadOnlyList<CrmDealView>>.Success(await ToDealViewsAsync(rows.Select(x => (x.d, x.s)).ToList(), ct));
        }, ct);

    public Task<Result<CrmDealView>> GetDealViewAsync(Guid tenantId, Guid dealId, CancellationToken ct = default) =>
        RunAsync("get_deal_view", tenantId, () => LoadDealViewAsync(tenantId, dealId, ct), ct);

    private async Task<Result<CrmDealView>> LoadDealViewAsync(Guid tenantId, Guid dealId, CancellationToken ct)
    {
        var row = await (
            from d in db.Deals.AsNoTracking()
            join s in db.Stages.AsNoTracking() on d.StageId equals s.Id
            where d.Id == dealId && d.TenantId == tenantId
            select new { d, s }).FirstOrDefaultAsync(ct);
        if (row is null) return NotFound<CrmDealView>("Deal not found.");
        return Result<CrmDealView>.Success((await ToDealViewsAsync([(row.d, row.s)], ct))[0]);
    }

    /// <summary>Adds each deal's company name and primary contact, with one query for the contacts and one for every name.</summary>
    private async Task<List<CrmDealView>> ToDealViewsAsync(List<(CrmDealRow Deal, CrmStageRow Stage)> rows, CancellationToken ct)
    {
        var dealIds = rows.Select(r => r.Deal.Id).ToList();
        var primaries = await db.DealParticipants.AsNoTracking()
            .Where(dp => dealIds.Contains(dp.DealId) && dp.Role == DealParticipantRoles.PrimaryContact)
            .Select(dp => new { dp.DealId, dp.ParticipantId })
            .ToListAsync(ct);
        var nameIds = rows.Where(r => r.Deal.OrganizationParticipantId is not null).Select(r => r.Deal.OrganizationParticipantId!.Value)
            .Concat(primaries.Select(p => p.ParticipantId)).Distinct().ToList();
        var names = await db.Participants.AsNoTracking()
            .Where(p => nameIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName })
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        return rows.Select(r =>
        {
            var primary = primaries.FirstOrDefault(p => p.DealId == r.Deal.Id);
            return new CrmDealView(
                ToDeal(r.Deal, r.Stage),
                r.Deal.OrganizationParticipantId is { } org && names.TryGetValue(org, out var orgName) ? orgName : null,
                primary?.ParticipantId,
                primary is not null && names.TryGetValue(primary.ParticipantId, out var contactName) ? contactName : null);
        }).ToList();
    }

    public Task<Result<CrmDealView>> UpdateDealAsync(Guid dealId, UpdateDealCommand command, CancellationToken ct = default) =>
        RunAsync("update_deal", command.TenantId, async () =>
        {
            var name = CrmText.Trim(command.Name);
            if (name is null) return Invalid<CrmDealView>("A deal needs a name.");
            if (name.Length > MaxTextLength) return Invalid<CrmDealView>($"A deal name is at most {MaxTextLength} characters.");
            var category = CrmText.Trim(command.ProductCategory);
            if (category is null) return Invalid<CrmDealView>("A deal needs a product category.");
            if (category.Length > 100) return Invalid<CrmDealView>("A product category is at most 100 characters.");
            if (command.EstimatedValue is < 0) return Invalid<CrmDealView>("A deal's value cannot be negative.");

            var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.TenantId == command.TenantId, ct);
            if (deal is null) return NotFound<CrmDealView>("Deal not found.");

            if (command.EstimatedValue is { } value && value != deal.EstimatedValue)
            {
                if (await db.DealLineItems.AnyAsync(l => l.DealId == deal.Id, ct))
                    return Invalid<CrmDealView>("This deal's value is the sum of its line items. Change the line items instead.");
                deal.EstimatedValue = value;
            }

            if (command.OrganizationParticipantId is { } orgId &&
                !await db.Participants.AnyAsync(p => p.Id == orgId && p.TenantId == command.TenantId && p.Kind == ParticipantKinds.Organization, ct))
                return NotFound<CrmDealView>("That company was not found.");

            if (command.PrimaryContactParticipantId is { } contactId)
            {
                if (!await db.Participants.AnyAsync(p => p.Id == contactId && p.TenantId == command.TenantId && p.Kind == ParticipantKinds.Person, ct))
                    return NotFound<CrmDealView>("That person was not found.");
                var current = await db.DealParticipants.Where(dp => dp.DealId == deal.Id && dp.Role == DealParticipantRoles.PrimaryContact).ToListAsync(ct);
                if (!current.Any(dp => dp.ParticipantId == contactId))
                {
                    db.DealParticipants.RemoveRange(current);
                    db.DealParticipants.Add(new CrmDealParticipantRow
                    {
                        Id = Guid.NewGuid(), TenantId = command.TenantId, DealId = deal.Id, ParticipantId = contactId, Role = DealParticipantRoles.PrimaryContact,
                    });
                }
            }

            deal.Name = name;
            deal.ProductCategory = category;
            deal.ExpectedCloseOn = command.ExpectedCloseOn;
            deal.OrganizationParticipantId = command.OrganizationParticipantId;
            deal.UpdatedAt = Now();
            await db.SaveChangesAsync(ct);
            return await LoadDealViewAsync(command.TenantId, deal.Id, ct);
        }, ct);

    // ----- Notes and tasks -----

    public Task<Result<IReadOnlyList<CrmActivityView>>> ListActivityViewsAsync(Guid tenantId, Guid? participantId, Guid? dealId, string? channel, bool openTasks, int limit, CancellationToken ct = default) =>
        RunAsync("list_activity_views", tenantId, async () =>
        {
            if (channel is not null && !ActivityChannels.All.Contains(channel))
                return Invalid<IReadOnlyList<CrmActivityView>>($"Unknown channel '{channel}'.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);

            var query = db.Activities.AsNoTracking().Where(a => a.TenantId == tenantId);
            if (participantId is { } pid) query = query.Where(a => a.ParticipantId == pid);
            if (dealId is { } did) query = query.Where(a => a.DealId == did);
            if (channel is not null) query = query.Where(a => a.Channel == channel);

            List<CrmActivityRow> rows;
            if (openTasks)
            {
                rows = await query.Where(a => a.Channel == ActivityChannels.Task && a.CompletedAt == null)
                    .OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.OccurredAt)
                    .Take(take).ToListAsync(ct);
            }
            else
            {
                rows = await query.OrderByDescending(a => a.OccurredAt).Take(take).ToListAsync(ct);
            }

            var participantIds = rows.Where(a => a.ParticipantId is not null).Select(a => a.ParticipantId!.Value).Distinct().ToList();
            var dealIds = rows.Where(a => a.DealId is not null).Select(a => a.DealId!.Value).Distinct().ToList();
            var participantNames = await db.Participants.AsNoTracking().Where(p => participantIds.Contains(p.Id))
                .Select(p => new { p.Id, p.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
            var dealNames = await db.Deals.AsNoTracking().Where(d => dealIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name }).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

            return Result<IReadOnlyList<CrmActivityView>>.Success(rows.Select(a => new CrmActivityView(
                ToActivity(a),
                a.ParticipantId is { } p && participantNames.TryGetValue(p, out var pn) ? pn : null,
                a.DealId is { } d && dealNames.TryGetValue(d, out var dn) ? dn : null)).ToList());
        }, ct);

    public Task<Result<CrmActivity>> CreateActivityAsync(CreateActivityCommand command, CancellationToken ct = default) =>
        RunAsync("create_activity", command.TenantId, async () =>
        {
            if (!ActivityChannels.All.Contains(command.Channel)) return Invalid<CrmActivity>($"Unknown channel '{command.Channel}'.");
            if (command.Direction is not null && !ActivityDirections.All.Contains(command.Direction)) return Invalid<CrmActivity>($"Unknown direction '{command.Direction}'.");
            if (command.Outcome is not null && !ActivityOutcomes.All.Contains(command.Outcome)) return Invalid<CrmActivity>($"Unknown outcome '{command.Outcome}'.");
            if (string.IsNullOrWhiteSpace(command.CreatedBy)) return Invalid<CrmActivity>("createdBy is required.");
            if (command.ParticipantId is null && command.DealId is null) return Invalid<CrmActivity>("An activity belongs to a person, a deal, or both.");
            var isTask = command.Channel == ActivityChannels.Task;
            if (!isTask && command.DueAt is not null) return Invalid<CrmActivity>("Only a task has a due date.");
            if (isTask && command.Outcome is not null) return Invalid<CrmActivity>("A task has no outcome until it is completed.");
            var subject = CrmText.Trim(command.Subject);
            var body = CrmText.Trim(command.Body);
            if (subject is null && body is null) return Invalid<CrmActivity>(isTask ? "A task needs a title." : "Say what happened.");
            if (subject is { Length: > MaxTextLength }) return Invalid<CrmActivity>($"A subject is at most {MaxTextLength} characters.");
            if (body is { Length: > MaxNotesLength }) return Invalid<CrmActivity>($"A note is at most {MaxNotesLength:N0} characters.");

            if (command.ParticipantId is { } pid &&
                !await db.Participants.AnyAsync(p => p.Id == pid && p.TenantId == command.TenantId, ct))
                return NotFound<CrmActivity>("Unknown participant.");
            if (command.DealId is { } did &&
                !await db.Deals.AnyAsync(d => d.Id == did && d.TenantId == command.TenantId, ct))
                return NotFound<CrmActivity>("Unknown deal.");

            var now = Now();
            var row = new CrmActivityRow
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                ParticipantId = command.ParticipantId,
                DealId = command.DealId,
                Channel = command.Channel,
                Direction = command.Direction,
                OccurredAt = command.OccurredAt.ToUniversalTime(),
                Subject = subject,
                Body = body,
                Outcome = command.Outcome,
                DueAt = command.DueAt?.ToUniversalTime(),
                CreatedBy = command.CreatedBy,
                CreatedAt = now,
            };
            db.Activities.Add(row);
            if (command.ParticipantId is { } touched && await db.Participants.FirstOrDefaultAsync(p => p.Id == touched, ct) is { } participant)
                participant.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Result<CrmActivity>.Success(ToActivity(row));
        }, ct);

    public Task<Result<CrmActivity>> CompleteTaskAsync(Guid tenantId, Guid activityId, CancellationToken ct = default) =>
        RunAsync("complete_task", tenantId, async () =>
        {
            var row = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId && a.TenantId == tenantId, ct);
            if (row is null) return NotFound<CrmActivity>("Task not found.");
            if (row.Channel != ActivityChannels.Task) return Invalid<CrmActivity>("Only a task can be completed.");
            if (row.CompletedAt is null)
            {
                row.CompletedAt = Now();
                row.Outcome = ActivityOutcomes.Completed;
                await db.SaveChangesAsync(ct);
            }
            return Result<CrmActivity>.Success(ToActivity(row));
        }, ct);

    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.Conflict);
}
