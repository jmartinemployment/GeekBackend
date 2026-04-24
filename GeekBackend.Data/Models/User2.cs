using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class User2
{
    public string Id { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Name { get; set; }

    public string? Image { get; set; }

    public string Plan { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();

    public virtual ICollection<Session1> Session1s { get; set; } = new List<Session1>();

    public virtual ICollection<Site> Sites { get; set; } = new List<Site>();
}
