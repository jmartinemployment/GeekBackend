using System.Text.RegularExpressions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// What every geek_voice migration must be true of before it is ever run against a database.
///
/// <para>
/// <b>Why this exists.</b> <c>0078_geek_voice_google_drive_provider.sql</c> altered
/// <c>geek_voice.mcp_connections</c>, and the table is <c>geek_voice.tenant_mcp_connections</c>. Postgres answers a missing table with
/// 42P01, and <c>SqlMigrationRunner</c> stops the application and rethrows - so the mistake did not fail one migration, it would have
/// stopped GeekRepository from starting and taken every later migration with it. It was found by reading, two days after it was
/// written, and nothing would have caught it earlier: it compiles, because it is a string.
/// </para>
///
/// <para>
/// A migration is the one kind of code here with no compiler and no caller. The checks below are the nearest thing it can have, and
/// they are cheap because the schema is small and the files are right there.
/// </para>
/// </summary>
public sealed class VoiceMigrationIntegrityTests
{
    private static readonly string SqlDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Migrations", "Sql"));

    /// <summary>Every migration's text with its comments stripped, so a table named only in a comment is not mistaken for a statement.</summary>
    private static IEnumerable<(string File, string Sql)> Migrations() =>
        Directory.EnumerateFiles(SqlDirectory, "*.sql")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), Regex.Replace(File.ReadAllText(path), @"--[^\n]*", string.Empty)));

    /// <summary>The geek_voice tables some migration creates. Only this schema: the rest are created by EF Core, not by this runner.</summary>
    private static HashSet<string> Created() =>
        Migrations()
            .SelectMany(m => Regex.Matches(m.Sql, @"CREATE TABLE(?:\s+IF NOT EXISTS)?\s+(geek_voice\.\w+)", RegexOptions.IgnoreCase))
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A migration may only alter or drop a geek_voice table that a migration creates. A name that is merely close enough to read past -
    /// mcp_connections for tenant_mcp_connections - is the whole failure, and it is invisible until the runner is pointed at a database.
    /// </summary>
    [Fact]
    public void Every_geek_voice_migration_names_a_table_that_some_migration_creates()
    {
        var created = Created();
        var wrong = new List<string>();

        foreach (var (file, sql) in Migrations())
        foreach (Match match in Regex.Matches(
            sql, @"(?:ALTER|DROP)\s+TABLE(?:\s+IF\s+EXISTS)?\s+(geek_voice\.\w+)", RegexOptions.IgnoreCase))
        {
            var table = match.Groups[1].Value;
            if (!created.Contains(table.ToLowerInvariant())) wrong.Add($"{file} names {table}");
        }

        Assert.True(wrong.Count == 0, $"These name a geek_voice table nothing creates: {string.Join("; ", wrong)}");
    }

    /// <summary>
    /// A migration that replaces an inline CHECK must drop the name Postgres actually gave it. A CHECK written inline on a column in
    /// <c>0060</c> is named table_column_check, so dropping anything else matches nothing, the original survives beside the new one,
    /// and the stricter of the two still rejects the value the migration was written to allow - while every layer above accepts it.
    ///
    /// <para>
    /// This asks only about columns that were created with an inline CHECK. A migration adding a brand-new constraint to a column that
    /// never had one - <c>0062</c>, <c>0063</c> and <c>0070</c> all do - has nothing to drop and is not the mistake being guarded against.
    /// </para>
    /// </summary>
    [Fact]
    public void A_migration_that_replaces_an_inline_check_drops_the_name_postgres_gave_it()
    {
        // Which columns were declared with a CHECK right on the column, per table: those are the ones Postgres auto-named.
        var inline = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (_, sql) in Migrations())
        foreach (Match table in Regex.Matches(
            sql, @"CREATE TABLE(?:\s+IF NOT EXISTS)?\s+geek_voice\.(\w+)\s*\((.*?)\n\);", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var columns = inline.TryGetValue(table.Groups[1].Value, out var existing)
                ? existing
                : inline[table.Groups[1].Value] = new HashSet<string>(StringComparer.Ordinal);

            // The first identifier inside each CHECK(...) is the column it constrains, which is what Postgres builds the name from.
            foreach (Match check in Regex.Matches(table.Groups[2].Value, @"CHECK\s*\(\s*(\w+)", RegexOptions.IgnoreCase))
                columns.Add(check.Groups[1].Value);
        }

        var missing = new List<string>();

        foreach (var (file, sql) in Migrations())
        foreach (Match added in Regex.Matches(
            sql,
            @"ALTER\s+TABLE(?:\s+IF\s+EXISTS)?\s+geek_voice\.(\w+)\s+ADD\s+CONSTRAINT\s+(\w+)\s+CHECK\s*\(\s*(\w+)",
            RegexOptions.IgnoreCase))
        {
            var (table, constraint, column) = (added.Groups[1].Value, added.Groups[2].Value, added.Groups[3].Value);

            if (!inline.TryGetValue(table, out var columns) || !columns.Contains(column)) continue;

            var dropsInline = Regex.IsMatch(
                sql, $@"DROP\s+CONSTRAINT(?:\s+IF\s+EXISTS)?\s+{Regex.Escape(table)}_{Regex.Escape(column)}_check", RegexOptions.IgnoreCase);

            if (!dropsInline)
                missing.Add($"{file} adds {constraint} over the inline CHECK on {table}.{column} without dropping {table}_{column}_check");
        }

        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }

    /// <summary>
    /// Every provider the product will accept must be one the database will accept. The two lists sit in different languages in
    /// different repositories, and the one that is wrong is silent: C# says yes and Postgres says no, at the moment an owner connects
    /// something. google_drive was in exactly that state.
    /// </summary>
    [Fact]
    public void Every_mcp_provider_the_product_accepts_is_one_the_database_accepts()
    {
        var sql = Migrations()
            .Where(m => m.Sql.Contains("tenant_mcp_connections", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Sql)
            .ToList();

        // The last CHECK written for this table is the one in force once every migration has run.
        var checks = sql
            .SelectMany(s => Regex.Matches(s, @"provider\s+IN\s*\(([^)]*)\)", RegexOptions.IgnoreCase))
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(checks);
        var allowed = Regex.Matches(checks[^1], @"'([^']+)'").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        foreach (var provider in GeekApplication.Models.Voice.VoiceMcpProviders.All)
            Assert.True(allowed.Contains(provider), $"C# accepts the provider {provider} and the database does not.");
    }
}
