using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class BucketsAnalytic
{
    public string Name { get; set; } = null!;

    public string Format { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
}
