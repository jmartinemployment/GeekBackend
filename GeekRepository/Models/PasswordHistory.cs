using System;
using System.Collections.Generic;

namespace GeekRepository.Models;

public partial class PasswordHistory
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual TeamMember TeamMember { get; set; } = null!;
}
