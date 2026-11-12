using System.Data;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Crm;

/// <summary>
/// Runs one repository operation inside one transaction that carries its tenant, so the database's
/// row-level security (0053_geek_crm_tenant_keys.sql) sees which tenant is asking. Everything the
/// operation writes lands together or not at all, and a read that forgot its tenant filter still
/// returns only that tenant's rows.
/// </summary>
public interface ICrmTenantScope
{
    /// <summary>Runs <paramref name="work"/>; commits when <paramref name="keep"/> says so for its result, otherwise rolls back.</summary>
    Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct);

    /// <summary>
    /// A platform operation (a tenant subscription): the same transaction, with the platform flag set as well as the
    /// seller's tenant, so it may read the platform table and write the seller's records in one step.
    /// </summary>
    Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct);
}

/// <summary>
/// The Postgres scope: BEGIN (READ COMMITTED), <c>SET LOCAL ROLE geek_crm_app</c> (0059_geek_crm_app_role.sql: a role that
/// is neither superuser nor BYPASSRLS, so row-level security applies whatever login GeekRepository connects with),
/// <c>set_config('app.tenant_id', tenant, true)</c> (SET LOCAL, so it ends with the transaction and never leaks to the
/// next use of the pooled connection), the work, then COMMIT or ROLLBACK. A failure in the work rolls back and propagates.
/// </summary>
public sealed class PostgresCrmTenantScope(CrmDbContext db) : ICrmTenantScope
{
    /// <summary>The role created by 0059_geek_crm_app_role.sql. A constant, never a parameter: it is part of the SQL text.</summary>
    public const string AppRole = "geek_crm_app";

    private const string SetRoleSql = "SET LOCAL ROLE geek_crm_app";

    public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => RunAsync(tenantId, false, work, keep, ct);

    public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => RunAsync(sellerTenantId, true, work, keep, ct);

    private async Task<T> RunAsync<T>(Guid tenantId, bool platform, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync(SetRoleSql, [], ct);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", [tenantId.ToString()], ct);
        if (platform) await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.platform', 'on', true)", [], ct);
        var result = await work();
        if (keep(result))
            await transaction.CommitAsync(ct);
        else
            await transaction.RollbackAsync(ct);
        return result;
    }
}
