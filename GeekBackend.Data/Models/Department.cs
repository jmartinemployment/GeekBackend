using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? IconName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual ICollection<UseCase> UseCases { get; set; } = new List<UseCase>();
}
