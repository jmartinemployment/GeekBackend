using System.Text.RegularExpressions;
using GeekRepository.Repositories.Crm;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Row-level security is skipped by a superuser and by a login with BYPASSRLS, whatever FORCE says. So every
/// tenant-scoped operation runs as a role that is neither (0059). The database proves it works (the integration test
/// reads current_user and rolbypassrls inside the scope); these tests pin the pieces that must stay true.
/// </summary>
public class CrmAppRoleTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GeekBackend.slnx")) && !Directory.Exists(Path.Combine(dir.FullName, "GeekRepository"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string Script(string path) => Regex.Replace(File.ReadAllText(Path.Combine(Root, path)), @"--[^\n]*", string.Empty);

    [Fact]
    public void The_app_role_cannot_log_in_and_has_no_power_that_skips_row_level_security()
    {
        var sql = Script("GeekRepository/Migrations/Sql/0059_geek_crm_app_role.sql");
        Assert.Contains($"CREATE ROLE {PostgresCrmTenantScope.AppRole} NOLOGIN NOSUPERUSER NOBYPASSRLS", sql, StringComparison.Ordinal);
        Assert.Contains($"GRANT {PostgresCrmTenantScope.AppRole} TO CURRENT_USER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BYPASSRLS;", sql.Replace("NOBYPASSRLS", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_role_may_use_every_geek_crm_table_now_and_later()
    {
        var sql = Script("GeekRepository/Migrations/Sql/0059_geek_crm_app_role.sql");
        Assert.Contains("GRANT USAGE ON SCHEMA geek_crm TO geek_crm_app", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA geek_crm TO geek_crm_app", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER DEFAULT PRIVILEGES IN SCHEMA geek_crm GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO geek_crm_app", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scope_switches_to_the_role_before_any_work_and_the_name_is_one_constant()
    {
        var source = Script("GeekRepository/Repositories/Crm/CrmTenantScope.cs");
        var role = source.IndexOf("ExecuteSqlRawAsync(SetRoleSql", StringComparison.Ordinal);
        var tenant = source.IndexOf("ExecuteSqlRawAsync(\"SELECT set_config('app.tenant_id'", StringComparison.Ordinal);
        var work = source.IndexOf("await work()", StringComparison.Ordinal);
        Assert.True(role > 0 && role < tenant && tenant < work, "SET LOCAL ROLE must come first, then the tenant, then the work.");
        Assert.Contains("\"SET LOCAL ROLE geek_crm_app\"", source, StringComparison.Ordinal);
        Assert.Equal("geek_crm_app", PostgresCrmTenantScope.AppRole);
    }
}
