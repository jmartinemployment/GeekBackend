using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TeamMemberJob
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string JobTitle { get; set; } = null!;

    public double HourlyRate { get; set; }

    public bool IsTipEligible { get; set; }

    public bool IsPrimary { get; set; }

    public bool OvertimeEligible { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual TeamMember TeamMember { get; set; } = null!;
}
