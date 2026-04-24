using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CircuitReset
{
    public int Id { get; set; }

    public string ServiceName { get; set; } = null!;

    public string ActionBySlackId { get; set; } = null!;

    public string? ActionByUsername { get; set; }

    public string PreviousState { get; set; } = null!;

    public string CurrentState { get; set; } = null!;

    public string? Metadata { get; set; }

    public DateTime ExecutedAt { get; set; }
}
