using System.Reflection;
using System.Text.RegularExpressions;
using GeekApplication.Models.Social;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Social;
using GeekRepository.Repositories.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeekBackend.Tests.Social;

/// <summary>
/// 0043_geek_social_schema.sql against the code that reads and writes it, with no database: the
/// CHECK lists against the vocabularies in GeekApplication, each table's columns against the EF
/// mapping, and each Dapper select list against the model it fills. Dapper drops a column the
/// model lacks and leaves a property the select lacks at its default without a word, so a
/// misspelt alias is a quietly wrong value; these tests are what makes it a failing build. The
/// integration suite runs the same script against a real Postgres when TEST_DATABASE_URL is set.
/// </summary>
public class SocialSchemaParityTests
{
    private static readonly string SqlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "GeekRepository", "Migrations", "Sql", "0043_geek_social_schema.sql"));

    /// <summary>The script with its comments removed, so prose that names a constraint cannot satisfy a test.</summary>
    private static string Sql => Regex.Replace(File.ReadAllText(SqlPath), @"--[^\n]*", string.Empty);

    private static SocialDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SocialDbContext>().UseNpgsql("Host=localhost;Database=never-opened").Options);

    private static string[] CheckList(string constraint)
    {
        var match = Regex.Match(Sql, $@"CONSTRAINT\s+{constraint}\s+CHECK\s*\(\s*\w+\s+IN\s*\(([^)]*)\)");
        Assert.True(match.Success, $"{constraint} is not in 0043 as a CHECK ... IN (...).");
        return Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }

    private static string[] Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData("ck_social_accounts_platform")]
    [InlineData("ck_engagement_events_platform")]
    [InlineData("ck_reply_drafts_platform")]
    [InlineData("ck_action_audit_platform")]
    public void Every_platform_check_lists_exactly_the_platforms_the_code_knows(string constraint) =>
        Assert.Equal(Sorted(SocialPlatforms.All), CheckList(constraint));

    [Fact]
    public void The_other_check_lists_match_their_vocabularies()
    {
        Assert.Equal(Sorted(SocialAccountStatuses.All), CheckList("ck_social_accounts_status"));
        Assert.Equal(Sorted(SocialPostKinds.All), CheckList("ck_social_posts_kind"));
        Assert.Equal(Sorted(EngagementKinds.All), CheckList("ck_engagement_events_kind"));
        Assert.Equal(Sorted(ReplyCategories.All), CheckList("ck_reply_drafts_category"));
        Assert.Equal(Sorted(ReplyDraftStatuses.All), CheckList("ck_reply_drafts_status"));
        Assert.Equal(Sorted(ActionKinds.All), CheckList("ck_action_audit_action"));
        Assert.Equal(Sorted(ActionOutcomes.All), CheckList("ck_action_audit_outcome"));
    }

    [Fact]
    public void The_auto_send_check_lists_every_category_except_the_ones_a_person_must_read()
    {
        var match = Regex.Match(Sql, @"auto_send_categories\s*<@\s*ARRAY\[([^\]]*)\]");
        Assert.True(match.Success, "ck_social_accounts_auto_send is not in 0043.");
        var listed = Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value);

        Assert.Equal(Sorted(ReplyCategories.All.Where(c => !ReplyCategories.NeverAutoSend.Contains(c))), Sorted(listed));
    }

    [Fact]
    public void One_answer_per_comment_index_holds_the_states_and_the_unknown_outcome_the_code_names()
    {
        var match = Regex.Match(Sql, @"ux_reply_drafts_one_answer_per_event\s+ON[^;]*WHERE\s+status\s+IN\s*\(([^)]*)\)\s+OR\s+failure_code\s*=\s*'([^']+)'");
        Assert.True(match.Success, "ux_reply_drafts_one_answer_per_event is not in 0043 in the expected form.");

        var held = Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value);
        Assert.Equal(
            Sorted(new[] { ReplyDraftStatuses.Pending, ReplyDraftStatuses.Approved, ReplyDraftStatuses.Sending, ReplyDraftStatuses.Sent }),
            Sorted(held));
        Assert.Equal(SocialFailureCodes.OutcomeUnknown, match.Groups[2].Value);
    }

    public static IEnumerable<object[]> Tables() => new[]
    {
        new object[] { typeof(SocialAccountRow), "social_accounts" },
        new object[] { typeof(SocialPostRow), "social_posts" },
        new object[] { typeof(EngagementEventRow), "engagement_events" },
        new object[] { typeof(ReplyDraftRow), "reply_drafts" },
        new object[] { typeof(ActionAuditRow), "action_audit" },
    };

    private static string[] ScriptColumns(string table)
    {
        var block = Regex.Match(Sql, $@"CREATE TABLE IF NOT EXISTS geek_social\.{table}\s*\((.*?)\n\);", RegexOptions.Singleline);
        Assert.True(block.Success, $"geek_social.{table} is not created by 0043.");
        return Regex.Matches(block.Groups[1].Value, @"^ {4}([a-z_]+)\s+(?:uuid|text\[\]|text|boolean|integer|bytea|timestamptz)\b", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_table_has_exactly_the_columns_its_row_class_maps(Type row, string table)
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(row)!;
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal(SocialDbContext.Schema, entity.GetSchema());

        var id = StoreObjectIdentifier.Table(table, SocialDbContext.Schema);
        var mapped = entity.GetProperties().Select(p => p.GetColumnName(id)!).OrderBy(c => c, StringComparer.Ordinal).ToArray();

        Assert.Equal(ScriptColumns(table), mapped);
    }

    [Fact]
    public void The_unique_indexes_the_context_names_are_the_ones_the_script_creates()
    {
        using var db = NewContext();
        var named = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetIndexes())
            .Select(i => i.GetDatabaseName()!)
            .ToArray();

        Assert.NotEmpty(named);
        foreach (var name in named)
            Assert.Contains($"INDEX IF NOT EXISTS {name}", Sql);
    }

    private static string Const(string name) =>
        (string)typeof(SocialRepository).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    private static string[] Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    public static IEnumerable<object[]> SelectLists()
    {
        yield return new object[] { "AccountColumns", "AccountReadRow", "social_accounts" };
        yield return new object[] { "PostColumns", nameof(SocialPost), "social_posts" };
        yield return new object[] { "DraftColumns", nameof(ReplyDraft), "reply_drafts" };
        yield return new object[] { "AuditColumns", nameof(ActionAudit), "action_audit" };
    }

    private static Type ModelNamed(string name) =>
        typeof(SocialRepository).GetNestedType(name, BindingFlags.NonPublic)
        ?? typeof(SocialPost).Assembly.GetType($"GeekApplication.Models.Social.{name}")!;

    [Theory]
    [MemberData(nameof(SelectLists))]
    public void A_select_list_names_real_columns_and_fills_every_property_of_its_model(string constant, string model, string table)
    {
        var select = Const(constant);
        var pairs = Regex.Matches(select, @"^\s*(\w+)\s+AS\s+(\w+)", RegexOptions.Multiline)
            .Select(m => (Column: m.Groups[1].Value, Alias: m.Groups[2].Value))
            .ToArray();

        Assert.Equal(Properties(ModelNamed(model)), pairs.Select(p => p.Alias).OrderBy(a => a, StringComparer.Ordinal).ToArray());
        var columns = ScriptColumns(table);
        foreach (var (column, alias) in pairs)
        {
            Assert.Contains(column, columns);
            Assert.Equal(alias, SocialDbContextPascal(column));
        }
    }

    /// <summary>"platform_account_id" to "PlatformAccountId", the property it should fill.</summary>
    private static string SocialDbContextPascal(string column) =>
        string.Concat(column.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    [Fact]
    public void The_event_select_fills_every_property_of_the_event_from_real_columns()
    {
        var select = Const("EventSelect");
        var aliases = Regex.Matches(select, @"\bAS\s+(\w+)").Select(m => m.Groups[1].Value).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        Assert.Equal(Properties(typeof(EngagementEvent)), aliases);

        var eventColumns = ScriptColumns("engagement_events");
        foreach (Match m in Regex.Matches(select, @"\be\.(\w+)"))
            Assert.Contains(m.Groups[1].Value, eventColumns);
        foreach (Match m in Regex.Matches(select, @"\ba\.(\w+)"))
            Assert.Contains(m.Groups[1].Value, ScriptColumns("social_accounts"));
    }
}
