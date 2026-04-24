using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ContentTemplate
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string? Description { get; set; }

    public string Prompt { get; set; } = null!;

    public string Fields { get; set; } = null!;

    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; }
}
