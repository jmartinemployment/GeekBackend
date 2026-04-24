using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class BucketsVector
{
    public string Id { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<VectorIndex> VectorIndices { get; set; } = new List<VectorIndex>();
}
