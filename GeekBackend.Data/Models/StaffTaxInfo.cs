using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class StaffTaxInfo
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string FilingStatus { get; set; } = null!;

    public bool MultipleJobs { get; set; }

    public double QualifyingChildrenAmount { get; set; }

    public double OtherDependentsAmount { get; set; }

    public double OtherIncome { get; set; }

    public double Deductions { get; set; }

    public double ExtraWithholding { get; set; }

    public string State { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual TeamMember TeamMember { get; set; } = null!;
}
