using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantProviderProfileEvent
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? ProfileId { get; set; }

    public string Provider { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string? Actor { get; set; }

    public int? ProfileVersion { get; set; }

    public string Outcome { get; set; } = null!;

    public string? CorrelationId { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RestaurantProviderProfile? Profile { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
