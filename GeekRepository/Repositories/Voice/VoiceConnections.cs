using Dapper;
using GeekRepository.Infrastructure;
using Npgsql;

namespace GeekRepository.Repositories.Voice;

/// <summary>How every geek_voice store opens a connection for one tenant, written once so no store can forget the tenant setting.</summary>
internal static class VoiceConnections
{
    /// <summary>
    /// An open connection that has run <c>set_config('app.current_tenant_id', tenant, false)</c>, so row-level security keys every
    /// tenant-scoped table on this tenant. A connection that cannot be opened or named is disposed and the fault raised.
    /// </summary>
    public static async Task<NpgsqlConnection> OpenForTenantAsync(IDbConnectionFactory db, Guid tenantId, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.CreateConnection();
        try
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('app.current_tenant_id', @TenantId, false);",
                new { TenantId = tenantId.ToString() },
                cancellationToken: ct));
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
