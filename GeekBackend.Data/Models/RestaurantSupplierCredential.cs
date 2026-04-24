using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantSupplierCredential
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? SyscoClientIdEncrypted { get; set; }

    public string? SyscoClientSecretEncrypted { get; set; }

    public string? SyscoCustomerIdEncrypted { get; set; }

    public string? SyscoMode { get; set; }

    public string? GfsClientIdEncrypted { get; set; }

    public string? GfsClientSecretEncrypted { get; set; }

    public string? GfsCustomerIdEncrypted { get; set; }

    public string? GfsMode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
