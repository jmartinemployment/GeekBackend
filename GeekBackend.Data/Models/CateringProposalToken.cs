using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CateringProposalToken
{
    public string Id { get; set; } = null!;

    public string JobId { get; set; } = null!;

    public string Token { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ViewedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual CateringEvent Job { get; set; } = null!;
}
