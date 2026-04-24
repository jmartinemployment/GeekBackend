using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CateringActivity
{
    public string Id { get; set; } = null!;

    public string JobId { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? Metadata { get; set; }

    public string ActorType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual CateringEvent Job { get; set; } = null!;
}
