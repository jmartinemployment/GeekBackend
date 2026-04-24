using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Message
{
    public string Id { get; set; } = null!;

    public string ThreadId { get; set; } = null!;

    public string Direction { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTime SentAt { get; set; }

    public virtual MessageThread Thread { get; set; } = null!;
}
