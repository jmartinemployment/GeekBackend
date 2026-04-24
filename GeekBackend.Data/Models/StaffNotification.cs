using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class StaffNotification
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string RecipientPinId { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
