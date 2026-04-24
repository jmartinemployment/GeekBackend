using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MessageTemplate
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string? Subject { get; set; }

    public string Body { get; set; } = null!;

    public string Variables { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
