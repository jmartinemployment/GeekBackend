using System.Text.Json;

namespace GeekApplication.Models.Crm;

// ----- Phase 3: what the AI voice receptionist needs (docs/GEEK-CRM-DATA-MODEL.md 4.7, 4.9, 4.16) -----

public sealed record CrmProject(
    Guid Id,
    Guid ParticipantId,
    Guid? DealId,
    Guid? CustomerHoldingId,
    string Status,
    DateOnly? StartOn,
    DateOnly? TargetEndOn,
    decimal EstimatedHours,
    decimal HoursPerWeek,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>The fields of a project; the same shape creates one and replaces one.</summary>
public sealed record ProjectDetails(
    string Status,
    DateOnly? StartOn,
    DateOnly? TargetEndOn,
    decimal EstimatedHours,
    decimal HoursPerWeek,
    Guid? DealId,
    Guid? CustomerHoldingId);

public sealed record CreateProjectCommand(Guid TenantId, Guid ParticipantId, ProjectDetails Details);

public sealed record UpdateProjectCommand(Guid TenantId, ProjectDetails Details);

/// <summary>One week (Monday to Sunday) of delivery capacity: hours booked against 40 base and 20 surge hours.</summary>
public sealed record CapacityWeek(DateOnly WeekStart, decimal BookedHours, decimal BaseHours, decimal SurgeHours, decimal BookedPct, decimal UpliftPct);

/// <summary>Booked hours per week, the uplift each week would carry, and the first week with base capacity free. A read, never a reservation.</summary>
public sealed record CrmCapacity(DateOnly AsOf, IReadOnlyList<CapacityWeek> Weeks, DateOnly? EarliestStartOn);

/// <summary>A product the caller wants, by its catalog code, with the size it is priced at (when the product is priced by size).</summary>
public sealed record IntakeProduct(string ProductCode, string? Size, decimal Quantity = 1m);

public sealed record IntakeMeeting(DateTime StartsAt, DateTime EndsAt, string? CalendarEventId);

/// <summary>
/// Everything one call leaves behind, written in one transaction and idempotent on the Twilio CallSid: the caller,
/// their contact points, a deal in its first stage with estimate line items, the call and the booked meeting.
/// </summary>
public sealed record ReceptionistIntakeCommand(
    Guid TenantId,
    string CallSid,
    string PhoneE164,
    string? FirstName,
    string? LastName,
    string? Email,
    string ProductCategory,
    JsonElement? Qualification,
    IReadOnlyList<IntakeProduct> Products,
    string? Summary,
    string? Transcript,
    string? RecordingUrl,
    int? DurationSeconds,
    DateTime OccurredAt,
    string Priority,
    IntakeMeeting? Meeting);

/// <summary>
/// Created is false when the CallSid was already taken in, which writes nothing. Problems lists each requested product
/// that could not be priced or found: the rest is written, and the list is how the caller knows what was left out.
/// </summary>
public sealed record ReceptionistIntakeResult(
    Guid ParticipantId,
    Guid DealId,
    Guid CallActivityId,
    Guid? MeetingActivityId,
    bool Created,
    IReadOnlyList<CrmDealLineItem> LineItems,
    decimal? EstimatedValue,
    decimal? RecurringValueMonthly,
    IReadOnlyList<string> Problems);

public static class CrmCapacityRules
{
    public const decimal BaseHoursPerWeek = 40m;
    public const decimal SurgeHoursPerWeek = 20m;
    public const decimal MaxHoursPerWeek = BaseHoursPerWeek + SurgeHoursPerWeek;
    public const int MaxWeeks = 52;
    public const int DefaultWeeks = 8;

    /// <summary>The Monday of the week a date falls in.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>The last day a project occupies: its target end, or its start plus enough weeks for its hours at its pace.</summary>
    public static DateOnly LastDay(DateOnly startOn, DateOnly? targetEndOn, decimal estimatedHours, decimal hoursPerWeek)
    {
        if (targetEndOn is { } end) return end;
        var weeks = hoursPerWeek <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(estimatedHours / hoursPerWeek));
        return startOn.AddDays(weeks * 7 - 1);
    }

    public static string? ValidateProject(ProjectDetails d)
    {
        if (!ProjectStatuses.All.Contains(d.Status)) return $"Unknown status '{d.Status}'. Use one of: {string.Join(", ", ProjectStatuses.All)}.";
        if (d.EstimatedHours < 0 || d.EstimatedHours > 99_999.99m || decimal.Round(d.EstimatedHours, 2) != d.EstimatedHours) return "Estimated hours is zero or more, with at most two decimals.";
        if (d.HoursPerWeek < 0 || d.HoursPerWeek > MaxHoursPerWeek || decimal.Round(d.HoursPerWeek, 2) != d.HoursPerWeek) return $"Hours per week is from 0 to {MaxHoursPerWeek}, with at most two decimals.";
        if (d.StartOn is { } start && d.TargetEndOn is { } end && end < start) return "A project cannot end before it starts.";
        if (IsBooked(d.Status) && d.StartOn is null) return "A scheduled or active project needs a start date.";
        return null;
    }

    /// <summary>A scheduled or active project holds hours; the others do not.</summary>
    public static bool IsBooked(string status) => status is ProjectStatuses.Scheduled or ProjectStatuses.Active;

    public static string? ValidateIntake(ReceptionistIntakeCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.CallSid) || c.CallSid.Length > 200) return "A call SID of at most 200 characters is required.";
        if (!CrmText.IsE164(c.PhoneE164)) return "A phone number in E.164 form is required.";
        if (string.IsNullOrWhiteSpace(c.ProductCategory) || c.ProductCategory.Length > 100) return "A product category is required.";
        if (!ActivityPriorities.All.Contains(c.Priority)) return $"Unknown priority '{c.Priority}'. Use one of: {string.Join(", ", ActivityPriorities.All)}.";
        if (c.Products is null) return "A product list is required (it may be empty).";
        foreach (var p in c.Products)
        {
            if (string.IsNullOrWhiteSpace(p.ProductCode)) return "Every product needs a code.";
            if (p.Size is not null && !EstimateSizes.All.Contains(p.Size)) return $"Unknown size '{p.Size}'.";
            if (p.Quantity <= 0 || p.Quantity > 99_999_999.99m || decimal.Round(p.Quantity, 2) != p.Quantity) return "Quantity is more than zero, with at most two decimals.";
        }
        if (c.Meeting is { } m && m.EndsAt <= m.StartsAt) return "A meeting must end after it starts.";
        if (c.DurationSeconds is < 0) return "Duration cannot be negative.";
        if (c.Email is not null && CrmText.Email(c.Email) is null) return "The email address is not valid.";
        return null;
    }
}
