using System.Text.RegularExpressions;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// 0050_geek_crm_schema.sql against the code, with no database: every CHECK list against its
/// vocabulary in GeekApplication/Models/Crm, and every table's columns against CrmDbContext's
/// mapping. A value or column one side has and the other lacks fails here, not on a live write.
/// </summary>
public class CrmSchemaParityTests
{
    private static readonly string SqlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "GeekRepository", "Migrations", "Sql", "0050_geek_crm_schema.sql"));

    private static string Sql => Regex.Replace(File.ReadAllText(SqlPath), @"--[^\n]*", string.Empty);

    private static CrmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql("Host=localhost;Database=never-opened").Options);

    private static string[] CheckList(string constraint)
    {
        var match = Regex.Match(Sql, $@"CONSTRAINT\s+{constraint}\s+CHECK\s*\((?:\s*\w+\s+IS\s+NULL\s+OR)?\s*\w+\s+IN\s*\(([^)]*)\)");
        Assert.True(match.Success, $"{constraint} is not in 0050 as a CHECK ... IN (...).");
        return Sorted(Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value));
    }

    private static string[] Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Every_check_list_matches_its_vocabulary()
    {
        Assert.Equal(Sorted(ParticipantKinds.All), CheckList("ck_participants_kind"));
        Assert.Equal(Sorted(LifecycleStages.All), CheckList("ck_participants_lifecycle_stage"));
        Assert.Equal(Sorted(ParticipantSources.All), CheckList("ck_participants_source"));
        Assert.Equal(Sorted(MaritalStatuses.All), CheckList("ck_persons_marital_status"));
        Assert.Equal(Sorted(EducationLevels.All), CheckList("ck_persons_education_level"));
        Assert.Equal(Sorted(OrganizationTypes.All), CheckList("ck_organizations_type"));
        Assert.Equal(Sorted(RevenueBands.All), CheckList("ck_organizations_revenue_band"));
        Assert.Equal(Sorted(ContactPointKinds.All), CheckList("ck_contact_points_kind"));
        Assert.Equal(Sorted(ContactPointLabels.All), CheckList("ck_contact_points_label"));
        Assert.Equal(Sorted(ParticipantLinkRoles.All), CheckList("ck_participant_links_role"));
        Assert.Equal(Sorted(StageOutcomes.All), CheckList("ck_stages_outcome"));
        Assert.Equal(Sorted(DealParticipantRoles.All), CheckList("ck_deal_participants_role"));
        Assert.Equal(Sorted(ActivityChannels.All), CheckList("ck_activities_channel"));
        Assert.Equal(Sorted(ActivityDirections.All), CheckList("ck_activities_direction"));
        Assert.Equal(Sorted(ActivityOutcomes.All), CheckList("ck_activities_outcome"));
    }

    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(CrmTenantRow), "tenants" },
        { typeof(CrmParticipantRow), "participants" },
        { typeof(CrmPersonRow), "persons" },
        { typeof(CrmOrganizationRow), "organizations" },
        { typeof(CrmContactPointRow), "contact_points" },
        { typeof(CrmParticipantLinkRow), "participant_links" },
        { typeof(CrmPipelineRow), "pipelines" },
        { typeof(CrmStageRow), "stages" },
        { typeof(CrmDealRow), "deals" },
        { typeof(CrmDealStageHistoryRow), "deal_stage_history" },
        { typeof(CrmDealParticipantRow), "deal_participants" },
        { typeof(CrmActivityRow), "activities" },
    };

    private static string[] ScriptColumns(string table)
    {
        var block = Regex.Match(Sql, $@"CREATE TABLE IF NOT EXISTS geek_crm\.{table}\s*\((.*?)\n\);", RegexOptions.Singleline);
        Assert.True(block.Success, $"geek_crm.{table} is not created by 0050.");
        return Sorted(Regex.Matches(block.Groups[1].Value,
                @"^ {4}([a-z_0-9]+)\s+(?:uuid|text|boolean|integer|timestamptz|date|jsonb|numeric)\b", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_table_has_exactly_the_columns_its_row_class_maps(Type row, string table)
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(row)!;
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal(CrmDbContext.Schema, entity.GetSchema());

        var id = StoreObjectIdentifier.Table(table, CrmDbContext.Schema);
        Assert.Equal(ScriptColumns(table), Sorted(entity.GetProperties().Select(p => p.GetColumnName(id)!)));
    }

    [Fact]
    public void The_seeded_tenant_is_the_one_the_code_defaults_to()
    {
        Assert.Contains($"'{CrmSeed.GeekAtYourSpotTenantId}', 'Geek @ Your Spot'", Sql);
    }

    [Fact]
    public void The_seeded_pipeline_has_the_agreed_stages_in_order()
    {
        var names = Regex.Matches(Sql, @"'[0-9a-f-]{36}',\s*'[0-9a-f-]{36}',\s*'[0-9a-f-]{36}',\s*'([^']+)',\s*(\d+),")
            .Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(x => x.Item2).Select(x => x.Item1).ToArray();
        Assert.Equal(["New", "Qualified", "Meeting booked", "Proposal sent", "Won", "Lost"], names);
    }
}
