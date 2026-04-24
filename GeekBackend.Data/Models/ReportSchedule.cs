using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ReportSchedule
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string SavedReportId { get; set; } = null!;

    public string Frequency { get; set; } = null!;

    public int? DayOfWeek { get; set; }

    public int? DayOfMonth { get; set; }

    public string TimeOfDay { get; set; } = null!;

    public string RecipientEmails { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime? LastSentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual SavedReport SavedReport { get; set; } = null!;
}
