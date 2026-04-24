using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ScheduleTemplate
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<TemplateShift> TemplateShifts { get; set; } = new List<TemplateShift>();
}
