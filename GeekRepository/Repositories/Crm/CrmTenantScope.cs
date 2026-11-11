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
}

/// <summary>
/// The Postgres scope: BEGIN (READ COMMITTED), <c>set_config('app.tenant_id', tenant, true)</c> (SET LOCAL, so
/// it ends with the transaction and never leaks to the next use of the pooled connection), the work,
/// then COMMIT or ROLLBACK. A failure in the work rolls back and propagates.
/// </summary>
public sealed class PostgresCrmTenantScope(CrmDbContext db) : ICrmTenantScope
{
    public async Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", [tenantId.ToString()], ct);
        var result = await work();
        if (keep(result))
            await transaction.CommitAsync(ct);
        else
            await transaction.RollbackAsync(ct);
        return result;
    }
}
