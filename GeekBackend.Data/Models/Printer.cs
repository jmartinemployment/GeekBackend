using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Printer
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Model { get; set; } = null!;

    public string MacAddress { get; set; } = null!;

    public string? IpAddress { get; set; }

    public string CloudprntId { get; set; } = null!;

    public string RegistrationToken { get; set; } = null!;

    public int PrintWidth { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastPollAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
