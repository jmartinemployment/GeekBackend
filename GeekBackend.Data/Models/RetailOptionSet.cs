using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailOptionSet
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual ICollection<RetailItemOptionSet> RetailItemOptionSets { get; set; } = new List<RetailItemOptionSet>();

    public virtual ICollection<RetailOption> RetailOptions { get; set; } = new List<RetailOption>();
}
