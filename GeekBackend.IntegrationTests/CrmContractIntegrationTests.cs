using System.Text.RegularExpressions;
using Npgsql;
using static GeekBackend.Tests.Crm.CrmXsdContract;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// The Geek-CRM contract (GeekApplication/Schemas/GeekCrm.xsd) against a real Postgres: the geek_crm
/// script is applied and the database's own catalog is compared with the XSD, so nothing is read out
/// of the script text with a regex. Columns, types and nullability come from information_schema;
/// each CHECK list from pg_constraint; single-column foreign keys from pg_constraint; unique rules
/// from pg_index. Only shipped parts are compared (a planned table or column must not exist yet).
/// Applies 0050 by itself, because that script is self-contained, instead of the whole folder.
/// Runs only with TEST_DATABASE_URL set.
/// </summary>
public sealed class CrmContractIntegrationTests
{
    private static readonly Dictionary<string, string> DataTypes = new(StringComparer.Ordinal)
    {
        ["uuid"] = "uuid",
        ["text"] = "text",
        ["boolean"] = "boolean",
        ["integer"] = "integer",
        ["timestamptz"] = "timestamp with time zone",
        ["date"] = "date",
        ["jsonb"] = "jsonb",
        ["numeric(12,2)"] = "numeric",
    };

    /// <summary>The schema scripts, in order. The NAICS load is data, not schema, and has its own test.</summary>
    private static readonly string[] SchemaScripts =
        ["0050_geek_crm_schema.sql", "0051_geek_crm_import_batch_and_property_retire.sql", "0053_geek_crm_tenant_keys.sql"];

    private static async Task<NpgsqlConnection> ApplyCrmScriptAsync()
    {
        var connection = new NpgsqlConnection(IntegrationTestConfig.DatabaseUrl);
        await connection.OpenAsync();
        foreach (var name in SchemaScripts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql", name);
            if (!File.Exists(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Migrations", "Sql", name));
            }
            await using var apply = new NpgsqlCommand(await File.ReadAllTextAsync(path), connection);
            await apply.ExecuteNonQueryAsync();
        }
        return connection;
    }

    private static async Task<List<object?[]>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        var rows = new List<object?[]>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    [Fact]
    public async Task Every_shipped_table_in_the_database_has_the_columns_types_and_nullability_the_xsd_names_and_no_planned_table_exists()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await using var connection = await ApplyCrmScriptAsync();

        var columns = await RowsAsync(connection,
            "select table_name, column_name, data_type, is_nullable, numeric_precision, numeric_scale from information_schema.columns where table_schema = 'geek_crm'");
        var byTable = columns.GroupBy(r => (string)r[0]!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        Assert.Equal(Sorted(Tables.Where(t => t.Shipped).Select(t => t.Table)), Sorted(byTable.Keys));

        var problems = new List<string>();
        foreach (var table in Tables.Where(t => t.Shipped))
        {
            var actual = byTable[table.Table];
            var names = Sorted(actual.Select(r => (string)r[1]!));
            if (!table.ExpectedColumns().SequenceEqual(names))
            {
                problems.Add($"{table.Table}: XSD [{string.Join(", ", table.ExpectedColumns())}] vs database [{string.Join(", ", names)}]");
                continue;
            }
            foreach (var column in table.Columns.Where(c => c.Shipped))
            {
                var row = actual.Single(r => (string)r[1]! == column.Column);
                var sqlType = SqlTypeOf(column.Type);
                if ((string)row[2]! != DataTypes[sqlType])
                    problems.Add($"{table.Table}.{column.Column}: XSD {column.Type} needs {DataTypes[sqlType]}, database has {row[2]}");
                if (sqlType == "numeric(12,2)" && (Convert.ToInt32(row[4], System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt32(row[5], System.Globalization.CultureInfo.InvariantCulture)) != (12, 2))
                    problems.Add($"{table.Table}.{column.Column}: money must be numeric(12,2), database has ({row[4]},{row[5]})");
                if (((string)row[3]! == "NO") != column.Required)
                    problems.Add($"{table.Table}.{column.Column}: XSD {(column.Required ? "requires" : "allows no value for")} it, database nullable = {row[3]}");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Every_check_list_in_the_database_equals_its_xsd_enumeration()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await using var connection = await ApplyCrmScriptAsync();

        var checks = (await RowsAsync(connection,
                "select conname, pg_get_constraintdef(oid) from pg_constraint where contype = 'c' and connamespace = 'geek_crm'::regnamespace"))
            .ToDictionary(r => (string)r[0]!, r => (string)r[1]!, StringComparer.Ordinal);

        foreach (var (constraint, type) in ConstraintToType)
        {
            Assert.True(checks.TryGetValue(constraint, out var definition), $"{constraint} is not in the database.");
            var values = Regex.Matches(definition!, "'([^']+)'::text").Select(m => m.Groups[1].Value);
            Assert.Equal(Enumeration(type), Sorted(values));
        }
    }

    [Fact]
    public async Task Every_foreign_key_and_unique_rule_in_the_database_is_a_reference_a_nesting_or_a_unique_in_the_xsd()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await using var connection = await ApplyCrmScriptAsync();

        var foreignKeys = await RowsAsync(connection, """
            select c.relname,
                   (select string_agg(a.attname::text, ',' order by u.ord)
                      from unnest(k.conkey) with ordinality u(attnum, ord)
                      join pg_attribute a on a.attrelid = k.conrelid and a.attnum = u.attnum),
                   p.relname,
                   (select string_agg(a.attname::text, ',' order by u.ord)
                      from unnest(k.confkey) with ordinality u(attnum, ord)
                      join pg_attribute a on a.attrelid = k.confrelid and a.attnum = u.attnum)
            from pg_constraint k
            join pg_class c on c.oid = k.conrelid
            join pg_class p on p.oid = k.confrelid
            where k.contype = 'f' and c.relnamespace = 'geek_crm'::regnamespace
            """);
        Assert.Equal(ForeignKeys(), Sorted(foreignKeys.Select(r => $"{r[0]}({r[1]})->{r[2]}({r[3]})")));

        var uniques = await RowsAsync(connection, """
            select t.relname, array_to_string(array_agg(a.attname::text order by a.attname::text), ',')
            from pg_index i
            join pg_class t on t.oid = i.indrelid
            join pg_attribute a on a.attrelid = t.oid and a.attnum = any(i.indkey)
            where i.indisunique and not i.indisprimary and t.relnamespace = 'geek_crm'::regnamespace
            group by i.indexrelid, t.relname
            """);
        Assert.Equal(Uniques(), Sorted(uniques.Select(r => $"{r[0]}({r[1]})")));
    }

    [Fact]
    public async Task The_naics_load_puts_the_census_2022_list_in_the_table_and_a_second_run_changes_nothing()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await using var connection = await ApplyCrmScriptAsync();
        var path = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql", "0052_geek_crm_naics_load.sql");
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "GeekRepository", "Migrations", "Sql", "0052_geek_crm_naics_load.sql"));
        }
        var script = await File.ReadAllTextAsync(path);

        for (var run = 0; run < 2; run++)
        {
            await using var load = new NpgsqlCommand(script, connection);
            await load.ExecuteNonQueryAsync();
            var count = await RowsAsync(connection, "select count(*), count(*) filter (where level = 6) from geek_crm.naics_codes");
            Assert.Equal((2125L, 1012L), ((long)count[0][0]!, (long)count[0][1]!));
        }
    }
}
