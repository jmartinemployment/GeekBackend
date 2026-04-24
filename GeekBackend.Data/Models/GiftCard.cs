using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class GiftCard
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string Type { get; set; } = null!;

    public decimal InitialBalance { get; set; }

    public decimal CurrentBalance { get; set; }

    public string Status { get; set; } = null!;

    public string? PurchasedBy { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientEmail { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<GiftCardRedemption> GiftCardRedemptions { get; set; } = new List<GiftCardRedemption>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
