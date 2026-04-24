using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MessageThread
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string CustomerId { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string? Subject { get; set; }

    public DateTime LastMessageAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
