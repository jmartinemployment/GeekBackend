using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class GiftCardRedemption
{
    public string Id { get; set; } = null!;

    public string GiftCardId { get; set; } = null!;

    public string? OrderId { get; set; }

    public decimal Amount { get; set; }

    public string? RedeemedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GiftCard GiftCard { get; set; } = null!;
}
