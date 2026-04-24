using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Subscription1
{
    public long Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public string Claims { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public string? ActionFilter { get; set; }
}
