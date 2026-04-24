using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class VectorIndex
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string BucketId { get; set; } = null!;

    public string DataType { get; set; } = null!;

    public int Dimension { get; set; }

    public string DistanceMetric { get; set; } = null!;

    public string? MetadataConfiguration { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual BucketsVector Bucket { get; set; } = null!;
}
