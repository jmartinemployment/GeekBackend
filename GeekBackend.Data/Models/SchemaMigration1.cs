using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class SchemaMigration1
{
    public long Version { get; set; }

    public DateTime? InsertedAt { get; set; }
}
