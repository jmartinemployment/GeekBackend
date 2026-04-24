using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailItemOptionSet
{
    public string Id { get; set; } = null!;

    public string RetailItemId { get; set; } = null!;

    public string OptionSetId { get; set; } = null!;

    public virtual RetailOptionSet OptionSet { get; set; } = null!;

    public virtual RetailItem RetailItem { get; set; } = null!;
}
