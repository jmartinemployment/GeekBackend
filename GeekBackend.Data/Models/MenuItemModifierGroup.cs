using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MenuItemModifierGroup
{
    public string Id { get; set; } = null!;

    public string MenuItemId { get; set; } = null!;

    public string ModifierGroupId { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public virtual MenuItem MenuItem { get; set; } = null!;

    public virtual ModifierGroup ModifierGroup { get; set; } = null!;
}
