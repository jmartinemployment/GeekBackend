using System.Text.Json;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Crm;

// Phase 3: projects and capacity, the activity list, and the receptionist's one-call intake
// (docs/GEEK-CRM-DATA-MODEL.md 4.16 and 5).
public sealed partial class CrmRepository
{
    private static readonly string[] BookedStatuses = [ProjectStatuses.Scheduled, ProjectStatuses.Active];

    // ----- Projects -----

    public Task<Result<IReadOnlyList<CrmProject>>> ListProjectsAsync(Guid tenantId, string? status, Guid? participantId, CancellationToken ct = default) =>
        RunAsync("list_projects", tenantId, async () =>
        {
            if (status is not null && !ProjectStatuses.All.Contains(status)) return Invalid<IReadOnlyList<CrmProject>>($"Unknown status '{status}'.");
            var query = db.Projects.AsNoTracking().Where(p => p.TenantId == tenantId);
            if (status is not null) query = query.Where(p => p.Status == status);
            if (participantId is { } pid) query = query.Where(p => p.ParticipantId == pid);
            var rows = await query.OrderBy(p => p.StartOn).ThenBy(p => p.CreatedAt).ToListAsync(ct);
            return Result<IReadOnlyList<CrmProject>>.Success(rows.Select(ToProject).ToList());
        }, ct);

    public Task<Result<CrmProject>> CreateProjectAsync(CreateProjectCommand command, CancellationToken ct = default) =>
        RunAsync("create_project", command.TenantId, async () =>
        {
            var problem = CrmCapacityRules.ValidateProject(command.Details);
            if (problem is not null) return Invalid<CrmProject>(problem);
            await LockCapacityAsync(command.TenantId, ct);

            var refusal = await CheckReferencesAsync(command.TenantId, command.ParticipantId, command.Details, ct);
            if (refusal is not null) return refusal;
            var over = await FirstOverbookedWeekAsync(command.TenantId, command.Details, null, ct);
            if (over is { } week) return OverCapacity(week);

            var now = Now();
            var row = new CrmProjectRow { Id = Guid.NewGuid(), TenantId = command.TenantId, ParticipantId = command.ParticipantId, CreatedAt = now };
            ApplyProject(row, command.Details, now);
            db.Projects.Add(row);
            await db.SaveChangesAsync(ct);
            return Result<CrmProject>.Success(ToProject(row));
        }, ct);

    public Task<Result<CrmProject>> UpdateProjectAsync(Guid projectId, UpdateProjectCommand command, CancellationToken ct = default) =>
        RunAsync("update_project", command.TenantId, async () =>
        {
            var problem = CrmCapacityRules.ValidateProject(command.Details);
            if (problem is not null) return Invalid<CrmProject>(problem);
            // Every change to a project's hours takes the lock, decreases as well as increases.
            await LockCapacityAsync(command.TenantId, ct);

            var row = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == command.TenantId, ct);
            if (row is null) return NotFound<CrmProject>("Project not found.");
            var refusal = await CheckReferencesAsync(command.TenantId, row.ParticipantId, command.Details, ct);
            if (refusal is not null) return refusal;

            // A decrease never fails: the same span with the same or fewer hours a week holds less than it did.
            var d = command.Details;
            var wasBooked = CrmCapacityRules.IsBooked(row.Status) && row.StartOn is not null;
            var shrinks = wasBooked && CrmCapacityRules.IsBooked(d.Status) && d.HoursPerWeek <= row.HoursPerWeek
                          && SpanOf(row.StartOn, row.TargetEndOn, row.EstimatedHours, row.HoursPerWeek) is var before
                          && SpanOf(d.StartOn, d.TargetEndOn, d.EstimatedHours, d.HoursPerWeek) is var after
                          && before is not null && after is not null && after.Value.First >= before.Value.First && after.Value.Last <= before.Value.Last;
            if (!shrinks)
            {
                var over = await FirstOverbookedWeekAsync(command.TenantId, d, projectId, ct);
                if (over is { } week) return OverCapacity(week);
            }

            ApplyProject(row, d, Now());
            await db.SaveChangesAsync(ct);
            return Result<CrmProject>.Success(ToProject(row));
        }, ct);

    /// <summary>A transaction-long lock per tenant, so two schedules cannot both claim the last surge hours.</summary>
    private async Task LockCapacityAsync(Guid tenantId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"geek_crm.capacity:" + tenantId}, 0))", ct);
    }

    private async Task<Result<CrmProject>?> CheckReferencesAsync(Guid tenantId, Guid participantId, ProjectDetails d, CancellationToken ct)
    {
        if (!await db.Participants.AnyAsync(p => p.Id == participantId && p.TenantId == tenantId, ct)) return NotFound<CrmProject>("Participant not found.");
        if (d.DealId is { } deal && !await db.Deals.AnyAsync(x => x.Id == deal && x.TenantId == tenantId, ct)) return NotFound<CrmProject>("Deal not found.");
        if (d.CustomerHoldingId is { } holding && !await db.CustomerHoldings.AnyAsync(x => x.Id == holding && x.TenantId == tenantId, ct)) return NotFound<CrmProject>("Holding not found.");
        return null;
    }

    private static Result<CrmProject> OverCapacity(DateOnly week) =>
        Result<CrmProject>.Failure($"That would take the week of {week:yyyy-MM-dd} past {CrmCapacityRules.MaxHoursPerWeek} hours ({CrmCapacityRules.BaseHoursPerWeek} base plus {CrmCapacityRules.SurgeHoursPerWeek} surge).", CrmFailureCodes.Conflict);

    private static (DateOnly First, DateOnly Last)? SpanOf(DateOnly? startOn, DateOnly? targetEndOn, decimal estimatedHours, decimal hoursPerWeek) =>
        startOn is { } start ? (CrmCapacityRules.WeekStart(start), CrmCapacityRules.WeekStart(CrmCapacityRules.LastDay(start, targetEndOn, estimatedHours, hoursPerWeek))) : null;

    /// <summary>Booked hours in each Monday-start week from other scheduled or active projects.</summary>
    private async Task<Dictionary<DateOnly, decimal>> BookedByWeekAsync(Guid tenantId, Guid? excluding, CancellationToken ct)
    {
        var rows = await db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId && BookedStatuses.Contains(p.Status) && p.StartOn != null && (excluding == null || p.Id != excluding))
            .ToListAsync(ct);
        var booked = new Dictionary<DateOnly, decimal>();
        foreach (var p in rows)
        {
            var span = SpanOf(p.StartOn, p.TargetEndOn, p.EstimatedHours, p.HoursPerWeek)!.Value;
            for (var week = span.First; week <= span.Last; week = week.AddDays(7))
                booked[week] = booked.GetValueOrDefault(week) + p.HoursPerWeek;
        }
        return booked;
    }

    private async Task<DateOnly?> FirstOverbookedWeekAsync(Guid tenantId, ProjectDetails d, Guid? excluding, CancellationToken ct)
    {
        if (!CrmCapacityRules.IsBooked(d.Status) || SpanOf(d.StartOn, d.TargetEndOn, d.EstimatedHours, d.HoursPerWeek) is not { } span) return null;
        var booked = await BookedByWeekAsync(tenantId, excluding, ct);
        for (var week = span.First; week <= span.Last; week = week.AddDays(7))
            if (booked.GetValueOrDefault(week) + d.HoursPerWeek > CrmCapacityRules.MaxHoursPerWeek) return week;
        return null;
    }

    private static void ApplyProject(CrmProjectRow row, ProjectDetails d, DateTime now)
    {
        row.Status = d.Status;
        row.StartOn = d.StartOn;
        row.TargetEndOn = d.TargetEndOn;
        row.EstimatedHours = d.EstimatedHours;
        row.HoursPerWeek = d.HoursPerWeek;
        row.DealId = d.DealId;
        row.CustomerHoldingId = d.CustomerHoldingId;
        row.UpdatedAt = now;
    }

    private static CrmProject ToProject(CrmProjectRow p) => new(
        p.Id, p.ParticipantId, p.DealId, p.CustomerHoldingId, p.Status, p.StartOn, p.TargetEndOn, p.EstimatedHours, p.HoursPerWeek, p.CreatedAt, p.UpdatedAt);

    // ----- Capacity -----

    public Task<Result<CrmCapacity>> GetCapacityAsync(Guid tenantId, int weeks, DateOnly? asOf, CancellationToken ct = default) =>
        RunAsync("get_capacity", tenantId, async () =>
        {
            if (weeks is < 1 or > CrmCapacityRules.MaxWeeks) return Invalid<CrmCapacity>($"Weeks is from 1 to {CrmCapacityRules.MaxWeeks}.");
            return Result<CrmCapacity>.Success(await ReadCapacityAsync(tenantId, weeks, asOf ?? DateOnly.FromDateTime(Now()), ct));
        }, ct);

    /// <summary>Read without a lock: the number is an estimate, because a project may be scheduled a moment later.</summary>
    private async Task<CrmCapacity> ReadCapacityAsync(Guid tenantId, int weeks, DateOnly asOf, CancellationToken ct)
    {
        var booked = await BookedByWeekAsync(tenantId, null, ct);
        var tiers = await db.PricingUpliftTiers.AsNoTracking().Where(t => t.TenantId == tenantId).OrderBy(t => t.BookedPctFrom).ToListAsync(ct);
        var result = new List<CapacityWeek>(weeks);
        var first = CrmCapacityRules.WeekStart(asOf);
        for (var i = 0; i < weeks; i++)
        {
            var week = first.AddDays(7 * i);
            var hours = booked.GetValueOrDefault(week);
            var pct = decimal.Round(hours / CrmCapacityRules.BaseHoursPerWeek * 100m, 2, MidpointRounding.AwayFromZero);
            var uplift = tiers.Where(t => t.BookedPctFrom <= pct).Select(t => t.UpliftPct).DefaultIfEmpty(0m).Max();
            result.Add(new CapacityWeek(week, hours, CrmCapacityRules.BaseHoursPerWeek, CrmCapacityRules.SurgeHoursPerWeek, pct, uplift));
        }
        var free = result.FirstOrDefault(w => w.BookedHours < w.BaseHours);
        return new CrmCapacity(asOf, result, free is null ? null : (free.WeekStart < asOf ? asOf : free.WeekStart));
    }

    // ----- Activities -----

    public Task<Result<IReadOnlyList<CrmActivity>>> ListActivitiesAsync(Guid tenantId, string? priority, string? channel, int limit, CancellationToken ct = default) =>
        RunAsync("list_activities", tenantId, async () =>
        {
            if (priority is not null && !ActivityPriorities.All.Contains(priority)) return Invalid<IReadOnlyList<CrmActivity>>($"Unknown priority '{priority}'.");
            if (channel is not null && !ActivityChannels.All.Contains(channel)) return Invalid<IReadOnlyList<CrmActivity>>($"Unknown channel '{channel}'.");
            var take = Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, MaxListLimit);
            var query = db.Activities.AsNoTracking().Where(a => a.TenantId == tenantId);
            if (priority is not null) query = query.Where(a => a.Priority == priority);
            if (channel is not null) query = query.Where(a => a.Channel == channel);
            var rows = await query.OrderByDescending(a => a.OccurredAt).Take(take).ToListAsync(ct);
            return Result<IReadOnlyList<CrmActivity>>.Success(rows.Select(ToActivity).ToList());
        }, ct);

    // ----- Intake -----

    public Task<Result<ReceptionistIntakeResult>> ReceptionistIntakeAsync(ReceptionistIntakeCommand command, CancellationToken ct = default) =>
        RunAsync("receptionist_intake", command.TenantId, async () =>
        {
            var problem = CrmCapacityRules.ValidateIntake(command);
            if (problem is not null) return Invalid<ReceptionistIntakeResult>(problem);
            if (command.Qualification is { } q && q.ValueKind != JsonValueKind.Object) return Invalid<ReceptionistIntakeResult>("Qualification answers are a JSON object keyed by question id.");
            if (!await TenantExistsAsync(command.TenantId, ct)) return NotFound<ReceptionistIntakeResult>("Unknown tenant.");

            var tenantId = command.TenantId;
            var callSid = command.CallSid.Trim();

            // Idempotent on the CallSid: a call already taken in writes nothing and answers what it wrote.
            var earlier = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId && a.ExternalRef == callSid
                && a.Channel == ActivityChannels.Call && a.Outcome == ActivityOutcomes.Answered, ct);
            if (earlier is not null)
            {
                if (earlier.DealId is not { } earlierDeal || earlier.ParticipantId is not { } earlierParticipant)
                    return Result<ReceptionistIntakeResult>.Failure("That call was recorded without a deal, so its intake cannot be repeated.", CrmFailureCodes.Conflict);
                var deal = await db.Deals.AsNoTracking().FirstAsync(d => d.Id == earlierDeal, ct);
                var meeting = await db.Activities.AsNoTracking().Where(a => a.TenantId == tenantId && a.ExternalRef == callSid && a.Channel == ActivityChannels.Meeting)
                    .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
                var existing = await LoadLineItemsAsync(deal, ct);
                return Result<ReceptionistIntakeResult>.Success(new ReceptionistIntakeResult(earlierParticipant, deal.Id, earlier.Id, meeting, false,
                    existing.Items, deal.EstimatedValue, deal.RecurringValueMonthly, []));
            }

            var now = Now();
            var today = DateOnly.FromDateTime(now);

            // The caller: the number finds the same person every time.
            var participantId = await db.ContactPoints.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Kind == ContactPointKinds.Phone && c.Value == command.PhoneE164)
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.CreatedAt).Select(c => (Guid?)c.ParticipantId).FirstOrDefaultAsync(ct);
            var first = CrmText.Trim(command.FirstName);
            var last = CrmText.Trim(command.LastName);
            if (participantId is null)
            {
                var name = string.Join(' ', new[] { first, last }.Where(n => n is not null));
                var participant = NewParticipant(tenantId, ParticipantKinds.Person, name.Length > 0 ? name : CrmText.FormatPhone(command.PhoneE164),
                    ParticipantSources.PhoneCall, "receptionist", null, now);
                db.Participants.Add(participant);
                db.Persons.Add(new CrmPersonRow { ParticipantId = participant.Id, FirstName = first, LastName = last });
                db.ContactPoints.Add(NewContactPoint(tenantId, participant.Id, ContactPointKinds.Phone, ContactPointLabels.Mobile, command.PhoneE164, true, now));
                participantId = participant.Id;
            }
            if (CrmText.Email(command.Email) is { } email
                && !await db.ContactPoints.AnyAsync(c => c.TenantId == tenantId && c.ParticipantId == participantId && c.Kind == ContactPointKinds.Email && c.Value == email, ct))
                db.ContactPoints.Add(NewContactPoint(tenantId, participantId.Value, ContactPointKinds.Email, ContactPointLabels.Work, email, false, now));
            await db.SaveChangesAsync(ct);

            // The deal, in the default pipeline's first stage.
            var pipeline = await db.Pipelines.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);
            if (pipeline is null) return NotFound<ReceptionistIntakeResult>("The tenant has no pipeline.");
            var stage = await db.Stages.AsNoTracking().Where(s => s.PipelineId == pipeline.Id).OrderBy(s => s.Position).FirstAsync(ct);
            var display = await db.Participants.AsNoTracking().Where(p => p.Id == participantId).Select(p => p.DisplayName).FirstAsync(ct);
            var dealRow = new CrmDealRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = $"{display}, {command.ProductCategory.Trim()}", PipelineId = pipeline.Id, StageId = stage.Id,
                ProductCategory = command.ProductCategory.Trim(), Qualification = command.Qualification?.GetRawText(), CreatedAt = now, UpdatedAt = now,
            };
            db.Deals.Add(dealRow);
            db.DealStageHistory.Add(new CrmDealStageHistoryRow { Id = Guid.NewGuid(), TenantId = tenantId, DealId = dealRow.Id, ToStageId = stage.Id, ChangedAt = now, ChangedBy = "receptionist" });
            db.DealParticipants.Add(new CrmDealParticipantRow { Id = Guid.NewGuid(), TenantId = tenantId, DealId = dealRow.Id, ParticipantId = participantId.Value, Role = DealParticipantRoles.PrimaryContact });

            var problems = new List<string>();
            await AddEstimateLinesAsync(command, dealRow, today, now, problems, ct);

            var callRow = NewIntakeActivity(command, tenantId, participantId.Value, dealRow.Id, ActivityChannels.Call, ActivityDirections.Inbound, ActivityOutcomes.Answered, now);
            callRow.Summary = command.Summary;
            callRow.Transcript = command.Transcript;
            callRow.RecordingUrl = command.RecordingUrl;
            callRow.DurationSeconds = command.DurationSeconds;
            db.Activities.Add(callRow);
            Guid? meetingId = null;
            if (command.Meeting is { } m)
            {
                var meetingRow = NewIntakeActivity(command, tenantId, participantId.Value, dealRow.Id, ActivityChannels.Meeting, null, ActivityOutcomes.Booked, now);
                meetingRow.OccurredAt = m.StartsAt.ToUniversalTime();
                meetingRow.StartsAt = m.StartsAt.ToUniversalTime();
                meetingRow.EndsAt = m.EndsAt.ToUniversalTime();
                meetingRow.CalendarEventId = m.CalendarEventId;
                db.Activities.Add(meetingRow);
                meetingId = meetingRow.Id;
            }
            await db.SaveChangesAsync(ct);

            var lines = await LoadLineItemsAsync(dealRow, ct);
            return Result<ReceptionistIntakeResult>.Success(new ReceptionistIntakeResult(participantId.Value, dealRow.Id, callRow.Id, meetingId, true,
                lines.Items, dealRow.EstimatedValue, dealRow.RecurringValueMonthly, problems));
        }, ct);

    private static CrmActivityRow NewIntakeActivity(ReceptionistIntakeCommand c, Guid tenantId, Guid participantId, Guid dealId, string channel, string? direction, string outcome, DateTime now) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ParticipantId = participantId, DealId = dealId, Channel = channel, Direction = direction,
        OccurredAt = c.OccurredAt.ToUniversalTime(), Outcome = outcome, ExternalRef = c.CallSid.Trim(), CreatedBy = "receptionist",
        CreatedAt = now, Priority = c.Priority,
    };

    /// <summary>
    /// The estimate: each requested product at the catalog price in effect for its size, raised by the demand uplift of the
    /// earliest week with free capacity, and a second use case with the same partner at the related-use-case percentage. What it
    /// was based on is stored with each line. A product that is unknown or has no price is left out and named in the problems.
    /// </summary>
    private async Task AddEstimateLinesAsync(ReceptionistIntakeCommand command, CrmDealRow deal, DateOnly today, DateTime now, List<string> problems, CancellationToken ct)
    {
        if (command.Products.Count == 0) return;
        var tenantId = command.TenantId;
        var rules = await db.PricingRules.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);
        var capacity = await ReadCapacityAsync(tenantId, CrmCapacityRules.DefaultWeeks, today, ct);
        var startWeek = capacity.EarliestStartOn is { } earliest ? capacity.Weeks.First(w => w.WeekStart == CrmCapacityRules.WeekStart(earliest)) : capacity.Weeks[^1];

        var priced = new List<(CrmProductRow Product, CrmProductPriceRow Price, IntakeProduct Request, decimal Base, bool Recurring)>();
        foreach (var request in command.Products)
        {
            var code = request.ProductCode.Trim();
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == code && p.IsActive, ct);
            if (product is null) { problems.Add($"'{code}' is not an active product of this tenant."); continue; }
            var recurring = product.Billing == BillingPeriods.Monthly;
            var price = await FindPriceInEffectAsync(tenantId, product.Id, request.Size, today, ct);
            var basePrice = recurring ? price?.RecurringPriceMonthly : price?.UnitPrice;
            if (price is null || basePrice is null) { problems.Add($"No {(recurring ? "monthly" : "one-time")} price is set for '{code}'{(request.Size is null ? string.Empty : $" ({request.Size})")}."); continue; }
            priced.Add((product, price, request, basePrice.Value, recurring));
        }

        // A related use case (the same partner) after the first, dearest one is priced at the related percentage.
        if (priced.Any(p => p.Product.PartnerParticipantId is not null) && rules is null)
            problems.Add("The tenant has no pricing rules, so related use cases were priced in full.");
        var seenPartners = new HashSet<Guid>();
        var items = new List<CrmDealLineItemRow>();
        foreach (var line in priced.OrderByDescending(p => p.Base))
        {
            var related = line.Product.PartnerParticipantId is { } partner && !seenPartners.Add(partner);
            var discount = related && rules is not null ? 100m - rules.RelatedUseCasePct : 0m;
            var workPriced = !line.Recurring && line.Price.Hours is not null;
            var uplift = workPriced ? startWeek.UpliftPct : 0m;
            var unit = decimal.Round(line.Base * (1m + uplift / 100m), 2, MidpointRounding.AwayFromZero);
            var inputs = JsonSerializer.Serialize(new
            {
                at = now,
                priceId = line.Price.Id,
                basePrice = line.Base,
                relatedUseCase = related,
                pricingRules = rules is null ? null : new { hourlyRate = rules.HourlyRate, relatedUseCasePct = rules.RelatedUseCasePct },
                capacity = new { weekStart = startWeek.WeekStart, bookedHours = startWeek.BookedHours, bookedPct = startWeek.BookedPct, upliftPct = startWeek.UpliftPct },
            });
            items.Add(new CrmDealLineItemRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DealId = deal.Id, ProductId = line.Product.Id, Quantity = line.Request.Quantity, UnitPrice = unit,
                IsRecurring = line.Recurring, DiscountPct = discount, Currency = Currencies.Usd, CreatedAt = now.AddTicks(items.Count), Size = line.Request.Size,
                Hours = line.Price.Hours, HourlyRate = rules?.HourlyRate, DemandUpliftPct = workPriced ? uplift : null, IsEstimate = true, EstimateInputs = inputs,
            });
        }
        if (items.Count == 0) return;
        db.DealLineItems.AddRange(items);
        deal.EstimatedValue = items.Where(i => !i.IsRecurring).Sum(i => CrmProductRules.LineTotal(i.Quantity, i.UnitPrice, i.DiscountPct));
        deal.RecurringValueMonthly = items.Where(i => i.IsRecurring).Sum(i => CrmProductRules.LineTotal(i.Quantity, i.UnitPrice, i.DiscountPct));
        deal.EstimateGivenAt = now;
    }
}
