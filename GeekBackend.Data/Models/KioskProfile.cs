using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class KioskProfile
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string PosMode { get; set; } = null!;

    public string WelcomeMessage { get; set; } = null!;

    public bool ShowImages { get; set; }

    public string EnabledCategories { get; set; } = null!;

    public bool RequireNameForOrder { get; set; }

    public int MaxIdleSeconds { get; set; }

    public bool EnableAccessibility { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
