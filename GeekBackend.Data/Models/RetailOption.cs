using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailOption
{
    public string Id { get; set; } = null!;

    public string OptionSetId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public decimal PriceAdjustment { get; set; }

    public int SortOrder { get; set; }

    public virtual RetailOptionSet OptionSet { get; set; } = null!;
}
